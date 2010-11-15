using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using log4net;
using Newtonsoft.Json.Linq;
using Raven.Abstractions.Data;
using Raven.Database.Backup;
using Raven.Database.Config;
using Raven.Database.Data;
using Raven.Database.Exceptions;
using Raven.Database.Extensions;
using Raven.Database.Impl;
using Raven.Database.Indexing;
using Raven.Database.Json;
using Raven.Database.LinearQueries;
using Raven.Database.Linq;
using Raven.Database.Plugins;
using Raven.Database.Storage;
using Raven.Database.Tasks;
using Raven.Http;
using Raven.Http.Exceptions;
using Index = Raven.Database.Indexing.Index;
using Task = Raven.Database.Tasks.Task;
using TransactionInformation = Raven.Http.TransactionInformation;

namespace Raven.Database
{
    public class DocumentDatabase : IResourceStore, IUuidGenerator
    {
        [ImportMany]
        public IEnumerable<AbstractAttachmentPutTrigger> AttachmentPutTriggers { get; set; }

        [ImportMany]
        public IEnumerable<AbstractAttachmentDeleteTrigger> AttachmentDeleteTriggers { get; set; }

        [ImportMany]
        public IEnumerable<AbstractAttachmentReadTrigger> AttachmentReadTriggers { get; set; }

        [ImportMany]
        public IEnumerable<AbstractPutTrigger> PutTriggers { get; set; }

        [ImportMany]
        public IEnumerable<AbstractDeleteTrigger> DeleteTriggers { get; set; }
        
        [ImportMany]
        public IEnumerable<AbstractIndexUpdateTrigger> IndexUpdateTriggers { get; set; }

        [ImportMany]
        public IEnumerable<AbstractReadTrigger> ReadTriggers { get; set; }

        [ImportMany]
        public AbstractDynamicCompilationExtension[] Extensions { get; set; }

        public DynamicQueryRunner DynamicQueryRunner
        {
            get { return dynamicQueryRunner; }
        }

        private AppDomain queriesAppDomain;
        private readonly WorkContext workContext;
        private readonly DynamicQueryRunner dynamicQueryRunner;
        private readonly SuggestionQueryRunner suggestionQueryRunner;

        private System.Threading.Tasks.Task backgroundWorkerTask;

        private readonly ILog log = LogManager.GetLogger(typeof(DocumentDatabase));

        private long currentEtagBase;

        public DocumentDatabase(InMemroyRavenConfiguration configuration)
        {
            Configuration = configuration;

            configuration.Container.SatisfyImportsOnce(this);

            workContext = new WorkContext
            {
            	IndexUpdateTriggers = IndexUpdateTriggers,
				ReadTriggers = ReadTriggers
            };
            dynamicQueryRunner = new DynamicQueryRunner(this);
            suggestionQueryRunner = new SuggestionQueryRunner(this);

            TransactionalStorage = configuration.CreateTransactionalStorage(workContext.HandleWorkNotifications);
            configuration.Container.SatisfyImportsOnce(TransactionalStorage);

            bool newDb;
            try
            {
                newDb = TransactionalStorage.Initialize(this);
            }
            catch (Exception)
            {
                TransactionalStorage.Dispose();
                throw;
            }

            TransactionalStorage.Batch(actions => currentEtagBase = actions.General.GetNextIdentityValue("Raven/Etag"));

            IndexDefinitionStorage = new IndexDefinitionStorage(
                configuration,
                TransactionalStorage,
                configuration.DataDirectory,
                configuration.Container.GetExportedValues<AbstractViewGenerator>(),
                Extensions);
            IndexStorage = new IndexStorage(IndexDefinitionStorage, configuration);

            workContext.IndexStorage = IndexStorage;
            workContext.TransactionaStorage = TransactionalStorage;
            workContext.IndexDefinitionStorage = IndexDefinitionStorage;


            try
            {
                InitializeTriggers();
                ExecuteStartupTasks();
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
            if (!newDb)
                return;

            OnNewlyCreatedDatabase();
        }

        private void InitializeTriggers()
        {
            PutTriggers.OfType<IRequiresDocumentDatabaseInitialization>().Apply(initialization => initialization.Initialize(this));
            DeleteTriggers.OfType<IRequiresDocumentDatabaseInitialization>().Apply(initialization => initialization.Initialize(this));
            ReadTriggers.OfType<IRequiresDocumentDatabaseInitialization>().Apply(initialization => initialization.Initialize(this));

            AttachmentPutTriggers.OfType<IRequiresDocumentDatabaseInitialization>().Apply(initialization => initialization.Initialize(this));
            AttachmentDeleteTriggers.OfType<IRequiresDocumentDatabaseInitialization>().Apply(initialization => initialization.Initialize(this));
            AttachmentReadTriggers.OfType<IRequiresDocumentDatabaseInitialization>().Apply(initialization => initialization.Initialize(this));
            
            IndexUpdateTriggers.OfType<IRequiresDocumentDatabaseInitialization>().Apply(initialization => initialization.Initialize(this));
        }

        private void ExecuteStartupTasks()
        {
            foreach (var task in Configuration.Container.GetExportedValues<IStartupTask>())
            {
                task.Execute(this);
            }
        }

        private void OnNewlyCreatedDatabase()
        {
            PutIndex("Raven/DocumentsByEntityName",
                     new IndexDefinition
                     {
                         Map =
                         @"from doc in docs 
where doc[""@metadata""][""Raven-Entity-Name""] != null 
select new { Tag = doc[""@metadata""][""Raven-Entity-Name""] };
",
                         Indexes = { { "Tag", FieldIndexing.NotAnalyzed } },
                         Stores = { { "Tag", FieldStorage.No } }
                     });
        }

        public DatabaseStatistics Statistics
        {
            get
            {
                var result = new DatabaseStatistics
                {
                    CountOfIndexes = IndexStorage.Indexes.Length,
                    Errors = workContext.Errors,
                    Triggers = PutTriggers.Select(x => new DatabaseStatistics.TriggerInfo { Name = x.ToString(), Type = "Put" })
                                .Concat(DeleteTriggers.Select(x => new DatabaseStatistics.TriggerInfo { Name = x.ToString(), Type = "Delete" }))
                                .Concat(ReadTriggers.Select(x => new DatabaseStatistics.TriggerInfo { Name = x.ToString(), Type = "Read" }))
                                .Concat(IndexUpdateTriggers.Select(x => new DatabaseStatistics.TriggerInfo { Name = x.ToString(), Type = "Index Update" }))
                                .ToArray()
                };

                TransactionalStorage.Batch(actions =>
                {
                    result.ApproximateTaskCount = actions.Tasks.ApproximateTaskCount;
                    result.CountOfDocuments = actions.Documents.GetDocumentsCount();
                    result.StaleIndexes = IndexStorage.Indexes
                        .Where(s =>
                        {
                            string entityName = null;
                            var abstractViewGenerator = IndexDefinitionStorage.GetViewGenerator(s);
                            if (abstractViewGenerator != null)
                                entityName = abstractViewGenerator.ForEntityName;

                            return actions.Staleness.IsIndexStale(s, null, entityName);
                        }).ToArray();
                    result.Indexes = actions.Indexing.GetIndexesStats().ToArray();
                });
                return result;
            }
        }

        IRaveHttpnConfiguration IResourceStore.Configuration
        {
            get { return Configuration;  }
        }

        public InMemroyRavenConfiguration Configuration
        {
            get;
            private set;
        }

        public ITransactionalStorage TransactionalStorage { get; private set; }

        public IndexDefinitionStorage IndexDefinitionStorage { get; private set; }

        public IndexStorage IndexStorage { get; private set; }

        #region IDisposable Members

        public void Dispose()
        {
            workContext.StopWork();
            UnloadQueriesAppDomain();
            TransactionalStorage.Dispose();
            IndexStorage.Dispose();
            if (backgroundWorkerTask != null)
                backgroundWorkerTask.Wait();
        }

        public void StopBackgroundWokers()
        {
            workContext.StopWork();
            backgroundWorkerTask.Wait();
        }

        public WorkContext WorkContext
        {
            get { return workContext; }
        }

        #endregion

        public void SpinBackgroundWorkers()
        {
            workContext.StartWork();
            backgroundWorkerTask = new System.Threading.Tasks.Task(
                new TaskExecuter(TransactionalStorage, workContext).Execute,
                TaskCreationOptions.LongRunning);
            backgroundWorkerTask.Start();
        }

		private static long sequentialUuidCounter;
        private QueryRunnerManager queryRunnerManager;

        public Guid CreateSequentialUuid()
		{
			var ticksAsBytes = BitConverter.GetBytes(currentEtagBase);
			Array.Reverse(ticksAsBytes);
			var increment = Interlocked.Increment(ref sequentialUuidCounter);
			var currentAsBytes = BitConverter.GetBytes(increment);
			Array.Reverse(currentAsBytes);
			var bytes = new byte[16];
			Array.Copy(ticksAsBytes, 0, bytes, 0, ticksAsBytes.Length);
			Array.Copy(currentAsBytes, 0, bytes, 8, currentAsBytes.Length);
			return bytes.TransfromToGuidWithProperSorting();
		}


    	public JsonDocument Get(string key, TransactionInformation transactionInformation)
        {
            JsonDocument document = null;
            TransactionalStorage.Batch(actions =>
            {
                document = actions.Documents.DocumentByKey(key, transactionInformation);
            });

            return new DocumentRetriever(null, ReadTriggers).ExecuteReadTriggers(document, transactionInformation,
                                                                                 ReadOperation.Load);
        }



        public PutResult Put(string key, Guid? etag, JObject document, JObject metadata, TransactionInformation transactionInformation)
        {
            if (string.IsNullOrEmpty(key))
            {
				// we no longer sort by the key, so it doesn't matter
				// that the key is no longer sequential
            	key = Guid.NewGuid().ToString();
            }
            RemoveReservedProperties(document);
            RemoveReservedProperties(metadata);
            Guid newEtag = Guid.Empty;
            TransactionalStorage.Batch(actions =>
            {
                if (key.EndsWith("/"))
                {
                    key += actions.General.GetNextIdentityValue(key);
                }
                metadata.Add("@id", new JValue(key));
                if (transactionInformation == null)
                {
                    AssertPutOperationNotVetoed(key, metadata, document, transactionInformation);
                    PutTriggers.Apply(trigger => trigger.OnPut(key, document, metadata, transactionInformation));

                    newEtag = actions.Documents.AddDocument(key, etag, document, metadata);
                    // We detect this by using the etags
                    // AddIndexingTask(actions, metadata, () => new IndexDocumentsTask { Keys = new[] { key } });
                    PutTriggers.Apply(trigger => trigger.AfterPut(key, document, metadata, newEtag, transactionInformation));
                }
                else
                {
                    newEtag = actions.Transactions.AddDocumentInTransaction(key, etag,
                                                     document, metadata, transactionInformation);
                }
                workContext.ShouldNotifyAboutWork();
            });

            TransactionalStorage
                .ExecuteImmediatelyOrRegisterForSyncronization(() => PutTriggers.Apply(trigger => trigger.AfterCommit(key, document, metadata, newEtag)));

            return new PutResult
            {
                Key = key,
                ETag = newEtag
            };
        }

        private void AddIndexingTask(IStorageActionsAccessor actions, JToken metadata, Func<Task> taskGenerator)
        {
            foreach (var indexName in IndexDefinitionStorage.IndexNames)
            {
                var viewGenerator = IndexDefinitionStorage.GetViewGenerator(indexName);
                if (viewGenerator == null)
                    continue;
                var entityName = metadata.Value<string>("Raven-Entity-Name");
                if (viewGenerator.ForEntityName != null &&
                        viewGenerator.ForEntityName != entityName)
                    continue;
                var task = taskGenerator();
                task.Index = indexName;
                actions.Tasks.AddTask(task, DateTime.UtcNow);
            }
        }

        private void AssertPutOperationNotVetoed(string key, JObject metadata, JObject document, TransactionInformation transactionInformation)
        {
            var vetoResult = PutTriggers
                .Select(trigger => new { Trigger = trigger, VetoResult = trigger.AllowPut(key, document, metadata, transactionInformation) })
                .FirstOrDefault(x => x.VetoResult.IsAllowed == false);
            if (vetoResult != null)
            {
                throw new OperationVetoedException("PUT vetoed by " + vetoResult.Trigger + " because: " + vetoResult.VetoResult.Reason);
            }
        }

        private void AssertAttachmentPutOperationNotVetoed(string key, JObject metadata, byte[] data)
        {
            var vetoResult = AttachmentPutTriggers
                .Select(trigger => new { Trigger = trigger, VetoResult = trigger.AllowPut(key, data, metadata) })
                .FirstOrDefault(x => x.VetoResult.IsAllowed == false);
            if (vetoResult != null)
            {
                throw new OperationVetoedException("PUT vetoed by " + vetoResult.Trigger + " because: " + vetoResult.VetoResult.Reason);
            }
        }

        private void AssertAttachmentDeleteOperationNotVetoed(string key)
        {
            var vetoResult = AttachmentDeleteTriggers
                .Select(trigger => new { Trigger = trigger, VetoResult = trigger.AllowDelete(key) })
                .FirstOrDefault(x => x.VetoResult.IsAllowed == false);
            if (vetoResult != null)
            {
                throw new OperationVetoedException("DELETE vetoed by " + vetoResult.Trigger + " because: " + vetoResult.VetoResult.Reason);
            }
        }

        private void AssertDeleteOperationNotVetoed(string key, TransactionInformation transactionInformation)
        {
            var vetoResult = DeleteTriggers
                .Select(trigger => new { Trigger = trigger, VetoResult = trigger.AllowDelete(key, transactionInformation) })
                .FirstOrDefault(x => x.VetoResult.IsAllowed == false);
            if (vetoResult != null)
            {
                throw new OperationVetoedException("DELETE vetoed by " + vetoResult.Trigger + " because: " + vetoResult.VetoResult.Reason);
            }
        }

        private static void RemoveReservedProperties(JObject document)
        {
            var toRemove = new HashSet<string>();
            foreach (var property in document.Properties())
            {
                if (property.Name.StartsWith("@"))
                    toRemove.Add(property.Name);
            }
            foreach (var propertyName in toRemove)
            {
                document.Remove(propertyName);
            }
        }

        public void Delete(string key, Guid? etag, TransactionInformation transactionInformation)
        {
            TransactionalStorage.Batch(actions =>
            {
                if (transactionInformation == null)
                {
                    AssertDeleteOperationNotVetoed(key, transactionInformation);

                    DeleteTriggers.Apply(trigger => trigger.OnDelete(key, transactionInformation));

                    JObject metadata;
                    if (actions.Documents.DeleteDocument(key, etag, out metadata))
                    {
                        AddIndexingTask(actions, metadata, () => new RemoveFromIndexTask { Keys = new[] { key } });
                        DeleteTriggers.Apply(trigger => trigger.AfterDelete(key, transactionInformation));
                    }
                }
                else
                {
                    actions.Transactions.DeleteDocumentInTransaction(transactionInformation, key, etag);
                }
                workContext.ShouldNotifyAboutWork();
            });
            TransactionalStorage
                .ExecuteImmediatelyOrRegisterForSyncronization(() => DeleteTriggers.Apply(trigger => trigger.AfterCommit(key)));
        }

        public void Commit(Guid txId)
        {
            try
            {
                TransactionalStorage.Batch(actions =>
                {
                    actions.Transactions.CompleteTransaction(txId, doc =>
                    {
                        // doc.Etag - represent the _modified_ document etag, and we already
                        // checked etags on previous PUT/DELETE, so we don't pass it here
                        if (doc.Delete)
                            Delete(doc.Key, null, null);
                        else
                            Put(doc.Key, null,
                                doc.Data,
                                doc.Metadata, null);
                    });
                    actions.Attachments.DeleteAttachment("transactions/recoveryInformation/" + txId, null);
                    workContext.ShouldNotifyAboutWork();
                });
            }
            catch (Exception e)
            {
                if (TransactionalStorage.HandleException(e))
                    return;
                throw;
            }
        }

        public void Rollback(Guid txId)
        {
            try
            {
                TransactionalStorage.Batch(actions =>
                {
                    actions.Transactions.RollbackTransaction(txId);
                    workContext.ShouldNotifyAboutWork();
                });
            }
            catch (Exception e)
            {
                if (TransactionalStorage.HandleException(e))
                    return;

                throw;
            }
        }

        public string PutIndex(string name, IndexDefinition definition)
        {
            switch (IndexDefinitionStorage.FindIndexCreationOptionsOptions(name, definition))
            {
                case IndexCreationOptions.Noop:
                    return name;
                case IndexCreationOptions.Update:
                    // ensure that the code can compile
                    new DynamicViewCompiler(name, definition, Extensions).GenerateInstance();
                    DeleteIndex(name);
                    break;
            }
            IndexDefinitionStorage.AddIndex(name, definition);
            IndexStorage.CreateIndexImplementation(name, definition);
            TransactionalStorage.Batch(actions => AddIndexAndEnqueueIndexingTasks(actions, name));
            return name;
        }

        private void AddIndexAndEnqueueIndexingTasks(IStorageActionsAccessor actions, string indexName)
        {
            actions.Indexing.AddIndex(indexName);
            workContext.ShouldNotifyAboutWork();
        }

        public QueryResult Query(string index, IndexQuery query)
        {
            var list = new List<JObject>();
            var stale = false;
            DateTime indexTimestamp = DateTime.MinValue;
            TransactionalStorage.Batch(
                actions =>
                {
                    string entityName = null;

                    var viewGenerator = IndexDefinitionStorage.GetViewGenerator(index);
                    if (viewGenerator != null)
                        entityName = viewGenerator.ForEntityName;

                    stale = actions.Staleness.IsIndexStale(index, query.Cutoff, entityName);
                    indexTimestamp = actions.Staleness.IndexLastUpdatedAt(index);
                    var indexFailureInformation = actions.Indexing.GetFailureRate(index);
                    if (indexFailureInformation.IsInvalidIndex)
                    {
                        throw new IndexDisabledException(indexFailureInformation);
                    }
                    var docRetriever = new DocumentRetriever(actions, ReadTriggers);
                    var collection = from queryResult in IndexStorage.Query(index, query, result => docRetriever.ShouldIncludeResultInQuery(result, GetIndexDefinition(index), query.FieldsToFetch))
                                     select docRetriever.RetrieveDocumentForQuery(queryResult, GetIndexDefinition(index), query.FieldsToFetch)
                                         into doc
                                         where doc != null
                                         select doc;

                    var transformerErrors = new List<string>();
                    IEnumerable<JObject> results;
                    if (viewGenerator != null && 
                        viewGenerator.TransformResultsDefinition != null)
                    {
                        var robustEnumerator = new RobustEnumerator
                        {
                            OnError =
                                (exception, o) =>
                                transformerErrors.Add(string.Format("Doc '{0}', Error: {1}", Index.TryGetDocKey(o),
                                                         exception.Message))
                        };
                        results =
                            robustEnumerator.RobustEnumeration(
                                collection.Select(x => new DynamicJsonObject(x.ToJson())),
                                source => viewGenerator.TransformResultsDefinition(docRetriever, source))
                                .Select(JsonExtensions.ToJObject);
                    }
                    else
                    {
                        results = collection.Select(x => x.ToJson());
                    }

                    list.AddRange(results);

                    if(transformerErrors.Count>0)
                    {
                        throw new InvalidOperationException("The transform results function failed.\r\n" + string.Join("\r\n", transformerErrors));
                    }

                });
            return new QueryResult
            {
                Results = list,
                IsStale = stale,
                SkippedResults = query.SkippedResults.Value,
                TotalResults = query.TotalSize.Value,
                IndexTimestamp = indexTimestamp
            };
        }

        public IEnumerable<string> QueryDocumentIds(string index, IndexQuery query, out bool stale)
        {
            bool isStale = false;
            HashSet<string> loadedIds = null;
            TransactionalStorage.Batch(
                actions =>
                {
                    isStale = actions.Staleness.IsIndexStale(index, query.Cutoff, null);
                    var indexFailureInformation = actions.Indexing.GetFailureRate(index)
;
                    if (indexFailureInformation.IsInvalidIndex)
                    {
                        throw new IndexDisabledException(indexFailureInformation);
                    }
                    loadedIds = new HashSet<string>(from queryResult in IndexStorage.Query(index, query, result => true)
                                                    select queryResult.Key);
                });
            stale = isStale;
            return loadedIds;
        }
        
        public void DeleteIndex(string name)
        {
            IndexDefinitionStorage.RemoveIndex(name);
            IndexStorage.DeleteIndex(name);
            //we may run into a conflict when trying to delete if the index is currently
            //busy indexing documents
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    TransactionalStorage.Batch(action =>
                    {
                        action.Indexing.DeleteIndex(name);

                        workContext.ShouldNotifyAboutWork();
                    });
                    return;
                }
                catch (ConcurrencyException)
                {
                    Thread.Sleep(100);
                }
            }
        }

        public Attachment GetStatic(string name)
        {
            Attachment attachment = null;
            TransactionalStorage.Batch(actions =>
            {
                attachment = actions.Attachments.GetAttachment(name);

                attachment = ProcessAttachmentReadVetoes(name, attachment);

                ExecuteAttachmentReadTriggers(name, attachment);
            });
            return attachment;
        }

        private Attachment ProcessAttachmentReadVetoes(string name, Attachment attachment)
        {
            if (attachment == null)
                return attachment;

            var foundResult = false;
            foreach (var attachmentReadTrigger in AttachmentReadTriggers)
            {
                if (foundResult)
                    break;
                var readVetoResult = attachmentReadTrigger.AllowRead(name, attachment.Data, attachment.Metadata,
                                                                     ReadOperation.Load);
                switch (readVetoResult.Veto)
                {
                    case ReadVetoResult.ReadAllow.Allow:
                        break;
                    case ReadVetoResult.ReadAllow.Deny:
                        attachment.Data = new byte[0];
                        attachment.Metadata = new JObject(
                            new JProperty("Raven-Read-Veto",
                                          new JObject(new JProperty("Reason", readVetoResult.Reason),
                                                      new JProperty("Trigger", attachmentReadTrigger.ToString())
                                              )));

                        foundResult = true;
                        break;
                    case ReadVetoResult.ReadAllow.Ignore:
                        attachment = null;
                        foundResult = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(readVetoResult.Veto.ToString());
                }
            }
            return attachment;
        }

        private void ExecuteAttachmentReadTriggers(string name, Attachment attachment)
        {
            if(attachment == null)
                return;

            foreach (var attachmentReadTrigger in AttachmentReadTriggers)
            {
                attachment.Data = attachmentReadTrigger.OnRead(name, attachment.Data,attachment.Metadata, ReadOperation.Load);
            }
        }

        public void PutStatic(string name, Guid? etag, byte[] data, JObject metadata)
        {
            Guid newEtag = Guid.Empty;
            TransactionalStorage.Batch(actions =>
            {
                AssertAttachmentPutOperationNotVetoed(name, metadata, data);

                AttachmentPutTriggers.Apply(trigger => trigger.OnPut(name, data, metadata));

                newEtag = actions.Attachments.AddAttachment(name, etag, data, metadata);

                AttachmentPutTriggers.Apply(trigger => trigger.AfterPut(name, data, metadata, newEtag));
            
                workContext.ShouldNotifyAboutWork();
            });

            TransactionalStorage
                .ExecuteImmediatelyOrRegisterForSyncronization(() => AttachmentPutTriggers.Apply(trigger => trigger.AfterCommit(name, data, metadata, newEtag)));

        }

        public void DeleteStatic(string name, Guid? etag)
        {
            TransactionalStorage.Batch(actions =>
            {
                AssertAttachmentDeleteOperationNotVetoed(name);

                AttachmentDeleteTriggers.Apply(x => x.OnDelete(name));

                actions.Attachments.DeleteAttachment(name, etag);

                AttachmentDeleteTriggers.Apply(x => x.AfterDelete(name));

                workContext.ShouldNotifyAboutWork();
            });

            TransactionalStorage
                .ExecuteImmediatelyOrRegisterForSyncronization(
                    () => AttachmentDeleteTriggers.Apply(trigger => trigger.AfterCommit(name)));

        }

        public JArray GetDocuments(int start, int pageSize, Guid? etag)
        {
            var list = new JArray();
            TransactionalStorage.Batch(actions =>
            {
                IEnumerable<JsonDocument> documents;
                if (etag == null)
                    documents = actions.Documents.GetDocumentsByReverseUpdateOrder(start);
                else
                    documents = actions.Documents.GetDocumentsAfter(etag.Value);
                var documentRetriever = new DocumentRetriever(actions, ReadTriggers);
                foreach (var doc in documents.Take(pageSize))
                {
                    var document = documentRetriever.ExecuteReadTriggers(doc, null,
						// here we want to have the Load semantic, not Query, because we need this to be
						// as close as possible to the full database contents
						ReadOperation.Load); 
                    if (document == null)
                        continue;
                    if (document.Metadata.Property("@id") == null)
                        document.Metadata.Add("@id", new JValue(doc.Key));

                    list.Add(document.ToJson());
                }
            });
            return list;
        }

		public AttachmentInformation[] GetAttachments(int start, int pageSize, Guid? etag)
		{
			AttachmentInformation[] documents = null;

			TransactionalStorage.Batch(actions =>
			{
				if (etag == null)
					documents = actions.Attachments.GetAttachmentsByReverseUpdateOrder(start).Take(pageSize).ToArray();
				else
					documents = actions.Attachments.GetAttachmentsAfter(etag.Value).Take(pageSize).ToArray();
				
			});
			return documents;
		}

        public JArray GetIndexNames(int start, int pageSize)
        {
            return new JArray(
                IndexDefinitionStorage.IndexNames.Skip(start).Take(pageSize)
                    .Select(s => new JValue(s))
                );
        }

        public JArray GetIndexes(int start, int pageSize)
        {
            return new JArray(
                IndexDefinitionStorage.IndexNames.Skip(start).Take(pageSize)
                    .Select(
                        indexName => new JObject
						{
							{"name", new JValue(indexName)},
							{"definition", JObject.FromObject(IndexDefinitionStorage.GetIndexDefinition(indexName))}
						})
                );
        }

        public PatchResult ApplyPatch(string docId, Guid? etag, PatchRequest[] patchDoc, TransactionInformation transactionInformation)
        {
            var result = PatchResult.Patched;
            TransactionalStorage.Batch(actions =>
            {
                var doc = actions.Documents.DocumentByKey(docId, transactionInformation);
                if (doc == null)
                {
                    result = PatchResult.DocumentDoesNotExists;
                }
                else if (etag != null && doc.Etag != etag.Value)
                {
                    throw new ConcurrencyException("Could not patch document '" + docId + "' because non current etag was used")
                    {
                        ActualETag = doc.Etag,
                        ExpectedETag = etag.Value,
                    };
                }
                else
                {
                    var jsonDoc = doc.ToJson();
                    new JsonPatcher(jsonDoc).Apply(patchDoc);
                    Put(doc.Key, doc.Etag, jsonDoc, doc.Metadata, transactionInformation);
                    result = PatchResult.Patched;
                }

                workContext.ShouldNotifyAboutWork();
            });

            return result;
        }

        public BatchResult[] Batch(ICollection<ICommandData> commands)
        {
            var results = new List<BatchResult>();

            log.DebugFormat("Executing {0} batched commands in a single transaction", commands.Count);
            TransactionalStorage.Batch(actions =>
            {
                foreach (var command in commands)
                {
                    command.Execute(this);
                    results.Add(new BatchResult
                    {
                        Method = command.Method,
                        Key = command.Key,
                        Etag = command.Etag,
                        Metadata = command.Metadata
                    });
                }
                workContext.ShouldNotifyAboutWork();
            });
            log.DebugFormat("Successfully executed {0} commands", commands.Count);
            return results.ToArray();
        }

        public bool HasTasks
        {
            get
            {
                bool hasTasks = false;
                TransactionalStorage.Batch(actions =>
                {
                    hasTasks = actions.Tasks.HasTasks;
                });
                return hasTasks;
            }
        }

        public long ApproximateTaskCount
        {
            get
            {
                long approximateTaskCount = 0;
                TransactionalStorage.Batch(actions =>
                {
                    approximateTaskCount = actions.Tasks.ApproximateTaskCount;
                });
                return approximateTaskCount;
            }
        }

        public void StartBackup(string backupDestinationDirectory)
        {
            var document = Get(BackupStatus.RavenBackupStatusDocumentKey, null);
            if (document != null)
            {
                var backupStatus = document.DataAsJson.JsonDeserialization<BackupStatus>();
                if (backupStatus.IsRunning)
                {
                    throw new InvalidOperationException("Backup is already running");
                }
            }
            Put(BackupStatus.RavenBackupStatusDocumentKey, null, JObject.FromObject(new BackupStatus
            {
                Started = DateTime.UtcNow,
                IsRunning = true,
            }), new JObject(), null);

            TransactionalStorage.StartBackupOperation(this, backupDestinationDirectory);
        }

        public static void Restore(RavenConfiguration configuration, string backupLocation, string databaseLocation)
        {
            using (var transactionalStorage = configuration.CreateTransactionalStorage(() => { }))
            {
                transactionalStorage.Restore(backupLocation, databaseLocation);
            }
        }

        public byte[] PromoteTransaction(Guid fromTxId)
        {
            var committableTransaction = new CommittableTransaction();
            var transmitterPropagationToken = TransactionInterop.GetTransmitterPropagationToken(committableTransaction);
            TransactionalStorage.Batch(
                actions =>
                    actions.Transactions.ModifyTransactionId(fromTxId, committableTransaction.TransactionInformation.DistributedIdentifier,
                                                TransactionManager.DefaultTimeout));
            return transmitterPropagationToken;
        }

        public void ResetIndex(string index)
        {
            var indexDefinition = IndexDefinitionStorage.GetIndexDefinition(index);
            if (indexDefinition == null)
                throw new InvalidOperationException("There is no index named: " + index);
            IndexStorage.DeleteIndex(index);
            IndexStorage.CreateIndexImplementation(index, indexDefinition);
            TransactionalStorage.Batch(actions =>
            {
                actions.Indexing.DeleteIndex(index);
                AddIndexAndEnqueueIndexingTasks(actions, index);
            });
        }

        public IndexDefinition GetIndexDefinition(string index)
        {
            return IndexDefinitionStorage.GetIndexDefinition(index);
        }

        public QueryResults ExecuteQueryUsingLinearSearch(LinearQuery query)
        {
            if(queriesAppDomain == null)
            {
                lock(this)
                {
                    if (queriesAppDomain == null)
                        InitailizeQueriesAppDomain();
                }
            }

            query.PageSize = Math.Min(query.PageSize, Configuration.MaxPageSize);

            RemoteQueryResults result;
            using (var remoteSingleQueryRunner = queryRunnerManager.CreateSingleQueryRunner(
                TransactionalStorage.TypeForRunningQueriesInRemoteAppDomain, 
                TransactionalStorage.StateForRunningQueriesInRemoteAppDomain))
            {
                result = remoteSingleQueryRunner.Query(query);
            }


            if(result.QueryCacheSize > 1024)
            {
                lock(this)
                {
                    if(queryRunnerManager.QueryCacheSize > 1024)
                        UnloadQueriesAppDomain();
                }
            }
            return new QueryResults
            {
                LastScannedResult = result.LastScannedResult,
                TotalResults = result.TotalResults,
                Errors = result.Errors,
                Results = result.Results.Select(JObject.Parse).ToArray()
            };

        }
        
        public QueryResult ExecuteDynamicQuery(string entityName, IndexQuery indexQuery)
        {
            return dynamicQueryRunner.ExecuteDynamicQuery(entityName, indexQuery);
        }

        public SuggestionQueryResult ExecuteSuggestionQuery(string index, SuggestionQuery suggestionQuery)
        {
            return suggestionQueryRunner.ExecuteSuggestionQuery(index, suggestionQuery);
        }

        private void UnloadQueriesAppDomain()
        {
            queryRunnerManager = null;
            if (queriesAppDomain != null)
                AppDomain.Unload(queriesAppDomain);
        }

        private void InitailizeQueriesAppDomain()
        {
            queriesAppDomain = AppDomain.CreateDomain("Queries", null, AppDomain.CurrentDomain.SetupInformation);
            queryRunnerManager = (QueryRunnerManager)queriesAppDomain.CreateInstanceAndUnwrap(typeof(QueryRunnerManager).Assembly.FullName, typeof(QueryRunnerManager).FullName);
        }

    }
}

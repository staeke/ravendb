using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Raven.Database.Config;
using Raven.Database.Extensions;
using Raven.Database.Indexing;
using Raven.Database.Json;
using Raven.Database.Linq;
using Raven.Database.Plugins;
using Raven.Http.Json;

namespace Raven.Database.Storage
{
	public class IndexDefinitionStorage
	{
		private const string IndexDefDir = "IndexDefinitions";

		private readonly ConcurrentDictionary<string, AbstractViewGenerator> indexCache =
			new ConcurrentDictionary<string, AbstractViewGenerator>(StringComparer.InvariantCultureIgnoreCase);
        private readonly ConcurrentDictionary<string, IndexDefinition> indexDefinitions =
            new ConcurrentDictionary<string, IndexDefinition>(StringComparer.InvariantCultureIgnoreCase);

		private readonly ILog logger = LogManager.GetLogger(typeof (IndexDefinitionStorage));
		private readonly string path;
        private readonly InMemroyRavenConfiguration configuration;
	    private readonly AbstractDynamicCompilationExtension[] extensions;

		public IndexDefinitionStorage(
            InMemroyRavenConfiguration configuration,
			ITransactionalStorage  transactionalStorage,
			string path, 
			IEnumerable<AbstractViewGenerator> compiledGenerators, 
			AbstractDynamicCompilationExtension[] extensions)
		{
		    this.configuration = configuration;
		    this.extensions = extensions;// this is used later in the ctor, so it must appears first
			this.path = Path.Combine(path, IndexDefDir);

			if (Directory.Exists(this.path) == false && configuration.RunInMemory == false)
				Directory.CreateDirectory(this.path);

            this.extensions = extensions;

            if (configuration.RunInMemory == false)
                ReadIndexesFromDisk();

            //compiled view generators always overwrite dynamic views
		    ReadIndexesFromCatalog(compiledGenerators, transactionalStorage);
		}

	    private void ReadIndexesFromCatalog(IEnumerable<AbstractViewGenerator> compiledGenerators, ITransactionalStorage transactionalStorage)
	    {
	        foreach (var generator in compiledGenerators)
	        {
	            var copy = generator;
	            var displayNameAtt = TypeDescriptor.GetAttributes(copy)
	                .OfType<DisplayNameAttribute>()
	                .FirstOrDefault();

	            var name = displayNameAtt != null ? displayNameAtt.DisplayName : copy.GetType().Name;

	            transactionalStorage.Batch(actions =>
	            {
	                if (actions.Indexing.GetIndexesStats().Any(x => x.Name == name))
	                    return;

	                actions.Indexing.AddIndex(name);
	            });

	            var indexDefinition = new IndexDefinition
	            {
	                Map = "Compiled map function: " + generator.GetType().AssemblyQualifiedName,
	                // need to supply this so the index storage will create map/reduce index
	                Reduce = generator.ReduceDefinition == null ? null : "Compiled reduce function: " + generator.GetType().AssemblyQualifiedName,
	                Indexes = generator.Indexes,
	                Stores = generator.Stores,
	                IsCompiled = true
	            };
	            indexCache.AddOrUpdate(name, copy, (s, viewGenerator) => copy);
	            indexDefinitions.AddOrUpdate(name, indexDefinition, (s1, definition) => indexDefinition);
	        }
	    }

	    private void ReadIndexesFromDisk()
	    {
	        foreach (var index in Directory.GetFiles(this.path, "*.index"))
	        {
	            try
	            {
	                AddAndCompileIndex(
	                    MonoHttpUtility.UrlDecode(Path.GetFileNameWithoutExtension(index)),
	                    JsonConvert.DeserializeObject<IndexDefinition>(File.ReadAllText(index), new JsonEnumConverter())
	                    );
	            }
	            catch (Exception e)
	            {
	                logger.Warn("Could not compile index " + index + ", skipping bad index", e);
	            }
	        }
	    }

	    public string[] IndexNames
		{
			get { return indexCache.Keys.ToArray(); }
		}

		public string AddIndex(string name, IndexDefinition indexDefinition)
		{
			DynamicViewCompiler transformer = AddAndCompileIndex(name, indexDefinition);
            if(configuration.RunInMemory == false)
			    File.WriteAllText(Path.Combine(path, transformer.Name + ".index"), JsonConvert.SerializeObject(indexDefinition, Formatting.Indented, new JsonEnumConverter()));
			return transformer.Name;
		}

		private DynamicViewCompiler AddAndCompileIndex(string name, IndexDefinition indexDefinition)
		{
			var transformer = new DynamicViewCompiler(name, indexDefinition, extensions);
			var generator = transformer.GenerateInstance();
			indexCache.AddOrUpdate(name, generator, (s, viewGenerator) => generator);
		    indexDefinitions.AddOrUpdate(name, indexDefinition, (s1, definition) =>
		    {
                if (definition.IsCompiled)
                    throw new InvalidOperationException("Index " + name + " is a compiled index, and cannot be replaced");
		        return indexDefinition;   
		    });
			logger.InfoFormat("New index {0}:\r\n{1}\r\nCompiled to:\r\n{2}", transformer.Name, transformer.CompiledQueryText,
			                  transformer.CompiledQueryText);
			return transformer;
		}

		public void RemoveIndex(string name)
		{
			AbstractViewGenerator ignoredViewGenerator;
			indexCache.TryRemove(name, out ignoredViewGenerator);
			IndexDefinition ignoredIndexDefinition;
			indexDefinitions.TryRemove(name, out ignoredIndexDefinition);
            if (configuration.RunInMemory)
                return;
			File.Delete(GetIndexPath(name));
			File.Delete(GetIndexSourcePath(name));
		}

		private string GetIndexSourcePath(string name)
		{
			return Path.Combine(path, MonoHttpUtility.UrlEncode(name) + ".index.cs");
		}

		private string GetIndexPath(string name)
		{
			return Path.Combine(path, MonoHttpUtility.UrlEncode(name) + ".index");
		}

		public IndexDefinition GetIndexDefinition(string name)
		{
		    IndexDefinition value;
			indexDefinitions.TryGetValue(name, out value);
		    return value;
		}

		public AbstractViewGenerator GetViewGenerator(string name)
		{
			AbstractViewGenerator value;
			if (indexCache.TryGetValue(name, out value) == false)
				return null;
			return value;
		}

		public IndexCreationOptions FindIndexCreationOptionsOptions(string name, IndexDefinition indexDef)
		{
			if (indexCache.ContainsKey(name))
			{
				return GetIndexDefinition(name).Equals(indexDef)
					? IndexCreationOptions.Noop
					: IndexCreationOptions.Update;
			}
			return IndexCreationOptions.Create;
		}

	    public bool Contains(string indexName)
	    {
            return indexDefinitions.ContainsKey(indexName);
	    }
	}
}

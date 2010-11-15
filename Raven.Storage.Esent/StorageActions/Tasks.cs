using System;
using System.Text;
using Microsoft.Isam.Esent.Interop;
using Raven.Database.Storage;
using Raven.Database.Tasks;
using Raven.Database.Extensions;
using Raven.Database.Json;

namespace Raven.Storage.Esent.StorageActions
{
	public partial class DocumentStorageActions : ITasksStorageActions
	{
	    public void AddTask(Task task, DateTime addedAt)
		{
			int actualBookmarkSize;
			var bookmark = new byte[SystemParameters.BookmarkMost];
			using (var update = new Update(session, Tasks, JET_prep.Insert))
			{
				Api.SetColumn(session, Tasks, tableColumnsCache.TasksColumns["task"], task.AsBytes());
				Api.SetColumn(session, Tasks, tableColumnsCache.TasksColumns["for_index"], task.Index, Encoding.Unicode);
				Api.SetColumn(session, Tasks, tableColumnsCache.TasksColumns["task_type"], task.Type, Encoding.Unicode);
				Api.SetColumn(session, Tasks, tableColumnsCache.TasksColumns["supports_merging"], task.SupportsMerging);
                Api.SetColumn(session, Tasks, tableColumnsCache.TasksColumns["added_at"], addedAt);

				update.Save(bookmark, bookmark.Length, out actualBookmarkSize);
			}
			Api.JetGotoBookmark(session, Tasks, bookmark, actualBookmarkSize);
		}


		public bool HasTasks
		{
			get
			{
				return Api.TryMoveFirst(session, Tasks);
			}
		}

		public long ApproximateTaskCount
		{
			get
			{
				if (Api.TryMoveFirst(session, Tasks) == false)
					return 0;
				var first = (int)Api.RetrieveColumnAsInt32(session, Tasks, tableColumnsCache.TasksColumns["id"]);
				if (Api.TryMoveLast(session, Tasks) == false)
					return 0;
				var last = (int)Api.RetrieveColumnAsInt32(session, Tasks, tableColumnsCache.TasksColumns["id"]);
				return last - first;
			}
		}

		public Task GetMergedTask(out int countOfMergedTasks)
		{
			Api.MoveBeforeFirst(session, Tasks);
			while (Api.TryMoveNext(session, Tasks))
			{
				var taskAsBytes = Api.RetrieveColumn(session, Tasks, tableColumnsCache.TasksColumns["task"]);
				var taskType = Api.RetrieveColumnAsString(session, Tasks, tableColumnsCache.TasksColumns["task_type"], Encoding.Unicode);
				try
				{
					Api.JetDelete(session, Tasks);
				}
				catch (EsentErrorException e)
				{
					if (e.Error != JET_err.WriteConflict)
						throw;
				}
				Task task;
				try
				{
					task = Task.ToTask(taskType, taskAsBytes);
				}
				catch (Exception e)
				{
					logger.ErrorFormat(e, "Could not create instance of a task: {0}", taskAsBytes);
					continue;
				}

				MergeSimilarTasks(task, out countOfMergedTasks);
				return task;
			}
			countOfMergedTasks = 0;
			return null;
		}

		public void MergeSimilarTasks(Task task, out int taskCount)
		{
			taskCount = 1;
			if (task.SupportsMerging == false)
				return;

			Api.JetSetCurrentIndex(session, Tasks, "mergables_by_task_type");
			Api.MakeKey(session, Tasks, true, MakeKeyGrbit.NewKey);
			Api.MakeKey(session, Tasks, task.Index, Encoding.Unicode, MakeKeyGrbit.None);
			Api.MakeKey(session, Tasks, task.Type, Encoding.Unicode, MakeKeyGrbit.None);
			// there are no tasks matching the current one, just return
			if (Api.TrySeek(session, Tasks, SeekGrbit.SeekEQ) == false)
			{
				return;
			}

			Api.MakeKey(session, Tasks, true, MakeKeyGrbit.NewKey);
			Api.MakeKey(session, Tasks, task.Index, Encoding.Unicode, MakeKeyGrbit.None);
			Api.MakeKey(session, Tasks, task.Type, Encoding.Unicode, MakeKeyGrbit.None);
			Api.JetSetIndexRange(session, Tasks, SetIndexRangeGrbit.RangeInclusive | SetIndexRangeGrbit.RangeUpperLimit);
			do
			{
				// esent index ranges are approximate, and we need to check them ourselves as well
				if (Api.RetrieveColumnAsBoolean(session, Tasks, tableColumnsCache.TasksColumns["supports_merging"]) == false)
					continue;
				if (Api.RetrieveColumnAsString(session, Tasks, tableColumnsCache.TasksColumns["for_index"]) != task.Index)
					continue;
				if (Api.RetrieveColumnAsString(session, Tasks, tableColumnsCache.TasksColumns["task_type"]) != task.Type)
					continue;

				try
				{
					var taskAsBytes = Api.RetrieveColumn(session, Tasks, tableColumnsCache.TasksColumns["task"]);
					var taskType = Api.RetrieveColumnAsString(session, Tasks, tableColumnsCache.TasksColumns["task_type"], Encoding.Unicode);
					Task existingTask;
					try
					{
						existingTask = Task.ToTask(taskType, taskAsBytes);
					}
					catch (Exception e)
					{
						logger.ErrorFormat(e, "Could not create instance of a task: {0}", taskAsBytes);
						Api.JetDelete(session, Tasks);
						continue;
					}
					if (task.TryMerge(existingTask) == false)
						continue;
					Api.JetDelete(session, Tasks);
					taskCount += 1;
				}
				catch (EsentErrorException e)
				{
					if (e.Error == JET_err.WriteConflict)
						continue;
					throw;
				}
			} while (
					task.SupportsMerging &&
					Api.TryMoveNext(session, Tasks)
				);
		}

	}
}

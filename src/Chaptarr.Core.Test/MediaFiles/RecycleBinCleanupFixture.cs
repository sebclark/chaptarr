using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;

namespace Chaptarr.Core.Test.MediaFiles
{
    // On a real install the recycle bin reached 386 GB / 27,706 files going back to 2024:
    // nothing ever scheduled CleanUpRecycleBinCommand, so the "keep 7 days" setting never ran.
    // And binned files kept their ORIGINAL timestamps, so once cleanup does run it must not
    // treat a file deleted today as years old.
    [TestFixture]
    public class RecycleBinCleanupFixture
    {
        private const string Bin = "/audiobooks/.recycle-bin";

        public class DiskProxy : DispatchProxy
        {
            public Dictionary<string, DateTime> Files { get; } = new();
            public Dictionary<string, DateTime> Folders { get; } = new();
            public List<string> Deleted { get; } = new();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod.Name)
                {
                    case nameof(IDiskProvider.GetFiles):
                        return Files.Keys.Where(f => f.StartsWith((string)args[0] + "/")).ToList();
                    case nameof(IDiskProvider.FileGetLastWrite):
                        return Files[(string)args[0]];
                    case nameof(IDiskProvider.FolderGetLastWrite):
                        return Folders.TryGetValue((string)args[0], out var t) ? t : DateTime.MinValue;
                    case nameof(IDiskProvider.DeleteFile):
                        Deleted.Add((string)args[0]);
                        return null;
                    case nameof(IDiskProvider.RemoveEmptySubfolders):
                        return null;
                    default:
                        throw new NotImplementedException($"Test proxy does not implement IDiskProvider.{targetMethod.Name}");
                }
            }
        }

        public class ConfigProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                return targetMethod.Name switch
                {
                    "get_RecycleBin" => Bin,
                    "get_RecycleBinCleanupDays" => 7,
                    "get_RssSyncInterval" => 15,
                    "get_BackupInterval" => 7,
                    _ => throw new NotImplementedException($"Test proxy does not implement IConfigService.{targetMethod.Name}")
                };
            }
        }

        [Test]
        public void cleanup_should_not_delete_a_file_binned_today_that_kept_an_old_timestamp()
        {
            var disk = DispatchProxy.Create<IDiskProvider, DiskProxy>();
            var proxy = (DiskProxy)(object)disk;
            var freshFolder = Bin + "/Author/Book";
            var staleFolder = Bin + "/Old Author/Old Book";

            // Binned minutes ago, but the move kept the file's 2024 timestamp.
            proxy.Files[freshFolder + "/book.m4b"] = new DateTime(2024, 8, 8, 0, 0, 0, DateTimeKind.Utc);
            proxy.Folders[freshFolder] = DateTime.UtcNow.AddMinutes(-5);

            // Genuinely old: both the file and the folder it sits in.
            proxy.Files[staleFolder + "/old.mp3"] = new DateTime(2024, 8, 8, 0, 0, 0, DateTimeKind.Utc);
            proxy.Folders[staleFolder] = DateTime.UtcNow.AddDays(-30);

            var subject = new RecycleBinProvider(null, disk, DispatchProxy.Create<IConfigService, ConfigProxy>(),
                LogManager.GetCurrentClassLogger());

            subject.Cleanup();

            Assert.That(proxy.Deleted, Is.EqualTo(new[] { staleFolder + "/old.mp3" }),
                "a file moved into the bin today must survive the 7-day window even when its own timestamp is old");
        }

        [Test]
        public void recycle_bin_cleanup_should_be_scheduled_daily()
        {
            var repository = DispatchProxy.Create<IScheduledTaskRepository, ScheduledTaskRepositoryRecorder>();
            var recorder = (ScheduledTaskRepositoryRecorder)(object)repository;
            var subject = new TaskManager(repository, DispatchProxy.Create<IConfigService, ConfigProxy>(),
                new CacheManager(), LogManager.GetCurrentClassLogger());

            subject.Handle(new ApplicationStartedEvent());

            var task = recorder.Upserted.SingleOrDefault(t => t.TypeName == typeof(CleanUpRecycleBinCommand).FullName);
            Assert.That(task, Is.Not.Null, "nothing schedules the recycle bin cleanup, so its 'keep N days' setting never runs");
            Assert.That(task.Interval, Is.EqualTo(24 * 60));
        }

        public class ScheduledTaskRepositoryRecorder : DispatchProxy
        {
            public List<ScheduledTask> Upserted { get; } = new();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod.Name)
                {
                    case nameof(IScheduledTaskRepository.All):
                        return new List<ScheduledTask>();
                    case nameof(IScheduledTaskRepository.Upsert):
                        Upserted.Add((ScheduledTask)args[0]);
                        return args[0];
                    case nameof(IScheduledTaskRepository.Delete):
                    case nameof(IScheduledTaskRepository.UpdateMany):
                        return null;
                    case nameof(IScheduledTaskRepository.GetDefinition):
                        return new ScheduledTask { TypeName = ((Type)args[0]).FullName, Interval = 60 };
                    default:
                        throw new NotImplementedException($"Test proxy does not implement IScheduledTaskRepository.{targetMethod.Name}");
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;

namespace Chaptarr.Core.Test.Queue
{
    // Every tracked-download update re-applied conversion status to the WHOLE queue with one
    // ConversionJobs query per item - N queries per update, N updates per refresh, so N^2 per
    // refresh. With ~1,900 queue items a real install recorded billions of lookups against an
    // EMPTY table, keeping Postgres busy. Status must come from one batched read per rebuild.
    [TestFixture]
    public class QueueServiceConversionStatusBatchFixture
    {
        private sealed class NullEventAggregator : IEventAggregator
        {
            public void PublishEvent<TEvent>(TEvent @event)
                where TEvent : class, IEvent
            {
            }
        }

        private class HistoryServiceProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == nameof(IHistoryService.Find) ||
                    targetMethod.Name == nameof(IHistoryService.FindByDownloadIds))
                {
                    return new List<EntityHistory>();
                }

                throw new NotImplementedException($"Test proxy does not implement IHistoryService.{targetMethod.Name}");
            }
        }

        private class ConversionTrackingProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == nameof(IConversionTrackingService.Get))
                {
                    return null;
                }

                throw new NotImplementedException($"Test proxy does not implement IConversionTrackingService.{targetMethod.Name}");
            }
        }

        public class ConversionJobServiceProxy : DispatchProxy
        {
            public int PerItemGetCalls { get; private set; }
            public Dictionary<string, ConversionJob> Jobs { get; } = new(StringComparer.Ordinal);

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod.Name)
                {
                    case "Get":
                        PerItemGetCalls++;
                        return Jobs.TryGetValue((string)args[0] ?? string.Empty, out var job) ? job : null;
                    case "GetNonCompletedByDownloadId":
                        return Jobs.Where(pair => pair.Value.Status != ConversionJobStatus.Completed)
                            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    default:
                        throw new NotImplementedException($"Test proxy does not implement IConversionJobService.{targetMethod.Name}");
                }
            }
        }

        private static TrackedDownload Tracked(string downloadId)
        {
            return new TrackedDownload
            {
                DownloadClient = 7,
                Protocol = DownloadProtocol.Torrent,
                IsTrackable = true,
                DownloadItem = new DownloadClientItem
                {
                    DownloadClientInfo = new DownloadClientItemClientInfo { Name = "Test Client", HasPostImportCategory = false },
                    DownloadId = downloadId,
                    Title = "Author - " + downloadId,
                    TotalSize = 100,
                    RemainingSize = 50,
                    RemainingTime = TimeSpan.FromMinutes(5),
                    OutputPath = new OsPath("/downloads/" + downloadId)
                },
                RemoteBook = new RemoteBook { Author = new Author { Id = 1, Name = "Author" } }
            };
        }

        private static (QueueService Service, ConversionJobServiceProxy Jobs) Build()
        {
            var jobService = DispatchProxy.Create<IConversionJobService, ConversionJobServiceProxy>();
            var jobs = (ConversionJobServiceProxy)(object)jobService;
            jobs.Jobs["download-2"] = new ConversionJob
            {
                DownloadId = "download-2",
                Status = ConversionJobStatus.Converting,
                TargetQualityId = 12,
                TargetQualityName = "M4B",
                Progress = 40m,
                Message = "converting"
            };
            jobs.Jobs["download-3"] = new ConversionJob { DownloadId = "download-3", Status = ConversionJobStatus.Completed };

            var service = new QueueService(
                new NullEventAggregator(),
                DispatchProxy.Create<IHistoryService, HistoryServiceProxy>(),
                DispatchProxy.Create<IConversionTrackingService, ConversionTrackingProxy>(),
                null,
                jobService);

            return (service, jobs);
        }

        [Test]
        public void refresh_should_not_query_conversion_jobs_once_per_queue_item()
        {
            var (service, jobs) = Build();

            service.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload>
            {
                Tracked("download-1"), Tracked("download-2"), Tracked("download-3")
            }));

            var queue = service.GetQueue().ToDictionary(item => item.DownloadId);

            Assert.Multiple(() =>
            {
                Assert.That(jobs.PerItemGetCalls, Is.EqualTo(0), "conversion status must be read once per rebuild, not per queue item");
                Assert.That(queue["download-2"].ConversionProgress, Is.EqualTo(40m), "a non-completed job must still overlay its item");
                Assert.That(queue["download-2"].ConvertToQuality, Is.EqualTo("M4B"));
                Assert.That(queue["download-1"].ConversionProgress, Is.Null);
                Assert.That(queue["download-3"].ConversionProgress, Is.Null, "a completed job must not overlay its item");
            });
        }

        [Test]
        public void a_single_download_update_should_not_query_every_queue_item()
        {
            var (service, jobs) = Build();
            service.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload>
            {
                Tracked("download-1"), Tracked("download-2"), Tracked("download-3")
            }));
            var before = jobs.PerItemGetCalls;

            service.Handle(new TrackedDownloadUpdatedEvent(Tracked("download-1")));

            Assert.Multiple(() =>
            {
                Assert.That(jobs.PerItemGetCalls - before, Is.EqualTo(0), "one download changing must not cost a query per queued download");
                Assert.That(service.GetQueue().Single(item => item.DownloadId == "download-2").ConversionProgress, Is.EqualTo(40m));
            });
        }
    }
}

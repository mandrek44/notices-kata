using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Mandro.Utils.Owin;
using Mandro.Utils.Web;

using Microsoft.Owin.Hosting;

using Moq;

using Newtonsoft.Json;

using NUnit.Framework;

using Owin;

using Raven.Client;
using Raven.Client.Embedded;

namespace NoticesKata
{
    public class NoticeSpecification
    {
        [Test]
        public void ShouldReceiveNotificationWithNoticeDetails()
        {
            // given
            // the system is running
            var emailServiceMock = new Mock<IEmailService>();
            var store = new EmbeddableDocumentStore() {RunInMemory = true};
            store.Initialize();
            var service = new NoticeService(emailServiceMock.Object, new RavenDbNoticeRepository(store));

            var port = 6786;
            service.Start(port);

            // there's a notice in the system
            using (var client = new WebClient())
            {
                client.Post(
                    string.Format("http://localhost:{0}/notice", port),
                    JsonConvert.SerializeObject(
                        new
                        {
                            Subject = "Test Notice",
                            Deadline = DateTime.Now.AddDays(2)
                        }));

                // when
                // I trigger the notification sending
                client.Post(
                    string.Format("http://localhost:{0}/notice/notification", port),
                    string.Empty);
            }

            // then
            // I should receive an email
            emailServiceMock.Verify(emailService => emailService.SendEmail("Notices", "Test Notice - 2 days left"));
        }
    }

    public class RavenDbNoticeRepository : INoticeRepository
    {
        private readonly IDocumentStore _store;

        public RavenDbNoticeRepository(IDocumentStore store)
        {
            _store = store;
        }

        public void Save(Notice newNotice)
        {
            using (var session = _store.OpenSession())
            {
                session.Store(newNotice);
                session.SaveChanges();
            }
        }

        public IEnumerable<Notice> GetNotices()
        {
            using (var session = _store.OpenSession())
            {
                return session.Query<Notice>();
            }
        }
    }

    public class RavenDbNoticeRepositoryTest
    {
        [Test]
        public void ShouldStoreNotices()
        {
            // given
            var store = new EmbeddableDocumentStore() { RunInMemory = true };
            store.Initialize();
            var repository = new RavenDbNoticeRepository(store);

            // when
            var deadline = DateTime.Now;
            repository.Save(new Notice()
            {
                Deadline = deadline,
                Subject = "Test Subject"
            });

            // then
            using (var session = store.OpenSession())
            {
                var notice = session.Load<Notice>("notices/1");
                Assert.That(notice, Is.Not.Null);
                Assert.That(notice.Subject, Is.EqualTo("Test Subject"));
                Assert.That(notice.Deadline, Is.EqualTo(deadline));
            }
        }

        [Test]
        public void ShouldRetrieveNotices()
        {
            // given
            var store = new EmbeddableDocumentStore() { RunInMemory = true };
            store.Initialize();
            var repository = new RavenDbNoticeRepository(store);

            DateTime deadline = DateTime.Now;
            using (var session = store.OpenSession())
            {
                session.Store(new Notice()
                {
                    Deadline = deadline,
                    Subject = "Test Subject"
                });
                session.SaveChanges();
            }

            // when
            var notices = repository.GetNotices();

            // then
            Assert.That(notices.Count(), Is.EqualTo(1));
            var notice = notices.Single();
            Assert.That(notice.Subject, Is.EqualTo("Test Subject"));
            Assert.That(notice.Deadline, Is.EqualTo(deadline));
        }
    }

    public interface IEmailService
    {
        void SendEmail(string subject, string body);
    }

    public class NoticeService
    {
        private readonly IEmailService _emailService;
        private readonly INoticeRepository _noticeRepository;

        public NoticeService(IEmailService emailService, INoticeRepository noticeRepository)
        {
            _emailService = emailService;
            _noticeRepository = noticeRepository;
        }

        public void Start(int port)
        {
            WebApp.Start("http://+:" + port, Configuration);
        }

        private void Configuration(IAppBuilder appBuilder)
        {
            var noticeService = new OwinService();
            noticeService.Post["/notice"].With(
                json =>
                {
                    var newNotice = JsonConvert.DeserializeObject<Notice>(json);
                    _noticeRepository.Save(newNotice);
                });

            noticeService.Post["/notice/notification"].With(
                _ =>
                {
                    var notices = _noticeRepository.GetNotices();
                    var noticeLines = notices.Select(notice => string.Format("{0} - {1} days left", notice.Subject, notice.DaysLeft));
                    var body = string.Join("\r\n", noticeLines);
                    _emailService.SendEmail("Notices", body);
                });

            appBuilder.TraceCalls();
            appBuilder.LogExceptions();
            appBuilder.UseOwinService(noticeService);
        }
    }

    public interface INoticeRepository
    {
        void Save(Notice newNotice);

        IEnumerable<Notice> GetNotices();
    }

    public class Notice
    {
        public int DaysLeft
        {
            get { return (int)(Deadline.Date - DateTime.Now.Date).TotalDays; }
        }

        public DateTime Deadline { get; set; }

        public string Subject { get; set; }
    }
}

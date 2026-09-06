using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class BookServiceFixture : CoreTest<BookService>
    {
        private Book _book;
        private Edition _edition;

        [SetUp]
        public void Setup()
        {
            _edition = new Edition
            {
                ForeignEditionId = "2382",
                Title = "The Good Sister",
                Monitored = true
            };

            _book = new Book
            {
                Id = 3624,
                AuthorMetadataId = 1,
                ForeignBookId = "2395",
                Editions = new List<Edition> { _edition }
            };

            Mocker.GetMock<IBookRepository>()
                .Setup(s => s.Get(It.IsAny<int>()))
                .Returns(_book);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsForRefresh(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns(new List<Edition>());
        }

        [Test]
        public void should_insert_editions_that_are_new()
        {
            Subject.AddBook(_book);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.InsertMany(It.Is<List<Edition>>(e => e.Count == 1)), Times.Once());
        }

        [Test]
        public void should_not_reinsert_an_edition_the_book_already_has()
        {
            // Re-adding a book already in the library -- an import list or an
            // external client asking again -- rebuilds its editions from the
            // metadata response, so they arrive with no Id. Inserting those
            // breaks the unique foreign edition ID, which surfaced as a bare
            // constraint error rather than anything a caller could act on.
            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsForRefresh(_book.Id, It.Is<List<string>>(ids => ids.Contains("2382"))))
                .Returns(new List<Edition>
                {
                    new Edition { Id = 91, BookId = _book.Id, ForeignEditionId = "2382" }
                });

            Subject.AddBook(_book);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.InsertMany(It.Is<List<Edition>>(e => e.Count == 0)), Times.Once());

            _edition.Id.Should().Be(91);
        }

        [Test]
        public void should_still_insert_a_new_edition_alongside_an_existing_one()
        {
            var added = new Edition { ForeignEditionId = "2383", Title = "The Good Sister" };
            _book.Editions = new List<Edition> { _edition, added };

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsForRefresh(_book.Id, It.IsAny<List<string>>()))
                .Returns(new List<Edition>
                {
                    new Edition { Id = 91, BookId = _book.Id, ForeignEditionId = "2382" }
                });

            Subject.AddBook(_book);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.InsertMany(It.Is<List<Edition>>(e => e.Single().ForeignEditionId == "2383")), Times.Once());
        }
    }
}

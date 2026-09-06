using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddBookFixture : CoreTest<AddBookService>
    {
        private Author _fakeAuthor;
        private Book _fakeBook;

        [SetUp]
        public void Setup()
        {
            _fakeAuthor = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .With(s => s.Metadata = Builder<AuthorMetadata>.CreateNew().Build())
                .Build();
        }

        private void GivenValidBook(string bookId, string editionId)
        {
            _fakeBook = Builder<Book>
                .CreateNew()
                .With(x => x.Editions = Builder<Edition>
                      .CreateListOfSize(1)
                      .TheFirst(1)
                      .With(e => e.ForeignEditionId = editionId)
                      .With(e => e.Monitored = true)
                      .BuildList())
                .Build();

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo(bookId))
                .Returns(Tuple.Create(_fakeAuthor.Metadata.Value.ForeignAuthorId,
                                      _fakeBook,
                                      new List<AuthorMetadata> { _fakeAuthor.Metadata.Value }));

            Mocker.GetMock<IAddAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns(_fakeAuthor);
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null))
                  .Returns<Author, NamingConfig>((c, n) => c.Name);
        }

        private Book BookToAdd(string editionId, string bookId, string authorId)
        {
            return new Book
            {
                ForeignBookId = bookId,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = editionId,
                        Monitored = true
                    }
                },
                AuthorMetadata = new AuthorMetadata
                {
                    ForeignAuthorId = authorId
                }
            };
        }

        [Test]
        public void should_be_able_to_add_a_book_without_passing_in_name()
        {
            var newBook = BookToAdd("edition", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            var book = Subject.AddBook(newBook);

            book.Title.Should().Be(_fakeBook.Title);
        }

        [Test]
        public void should_monitor_a_book_the_library_already_has_unmonitored()
        {
            // A book can already be in the library without being tracked --
            // pulled in by an author refresh or a library scan. Adding it is a
            // request to start tracking it, and copying the stored fields back
            // over the request left it exactly as unmonitored as before, so the
            // caller kept asking.
            var newBook = BookToAdd("edition", "book", "author");
            newBook.Monitored = true;

            GivenValidBook("book", "edition");
            GivenValidPath();

            Mocker.GetMock<IBookService>()
                .Setup(s => s.FindById(It.IsAny<string>()))
                .Returns(new Book { Id = 3624, Monitored = false });

            Subject.AddBook(newBook);

            Mocker.GetMock<IBookService>()
                .Verify(v => v.AddBook(It.Is<Book>(b => b.Id == 3624 && b.Monitored), It.IsAny<bool>()), Times.Once());
        }

        [Test]
        public void should_keep_the_requested_search_option_for_an_existing_book()
        {
            // SearchForNewBook is read back off the stored row after the refresh, so
            // adopting the existing row's AddOptions meant an explicit add was
            // monitored and then never searched for.
            var newBook = BookToAdd("edition", "book", "author");
            newBook.AddOptions = new AddBookOptions { SearchForNewBook = true };

            GivenValidBook("book", "edition");
            GivenValidPath();

            Mocker.GetMock<IBookService>()
                .Setup(s => s.FindById(It.IsAny<string>()))
                .Returns(new Book { Id = 3624, AddOptions = new AddBookOptions { SearchForNewBook = false } });

            Subject.AddBook(newBook);

            Mocker.GetMock<IBookService>()
                .Verify(v => v.AddBook(It.Is<Book>(b => b.AddOptions.SearchForNewBook), It.IsAny<bool>()), Times.Once());
        }

        [Test]
        public void should_throw_if_book_cannot_be_found()
        {
            var newBook = BookToAdd("edition", "book", "author");

            Mocker.GetMock<IProvideBookInfo>()
                  .Setup(s => s.GetBookInfo("book"))
                  .Throws(new BookNotFoundException("edition"));

            Assert.Throws<ValidationException>(() => Subject.AddBook(newBook));

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Books
{
    public interface IAddBookService
    {
        Book AddBook(Book book, bool doRefresh = true);
        List<Book> AddBooks(List<Book> books, bool doRefresh = true);
    }

    public class AddBookService : IAddBookService
    {
        private readonly IAuthorService _authorService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IBookService _bookService;
        private readonly IProvideBookInfo _bookInfo;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly Logger _logger;

        public AddBookService(IAuthorService authorService,
                               IAddAuthorService addAuthorService,
                               IBookService bookService,
                               IProvideBookInfo bookInfo,
                               IImportListExclusionService importListExclusionService,
                               Logger logger)
        {
            _authorService = authorService;
            _addAuthorService = addAuthorService;
            _bookService = bookService;
            _bookInfo = bookInfo;
            _importListExclusionService = importListExclusionService;
            _logger = logger;
        }

        public Book AddBook(Book book, bool doRefresh = true)
        {
            _logger.Debug($"Adding book {book}");

            book = AddSkyhookData(book);

            // we allow adding extra editions, so check if the book already exists
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                // Asking to add a book that is already there is asking for it to
                // be tracked, not for the request to be dropped -- the row can
                // already exist unmonitored, pulled in by an author refresh or a
                // library scan. UseDbFieldsFrom adopts the stored monitoring
                // state, so put the caller's choice back over it.
                var monitored = book.Monitored;
                var requestedOptions = book.AddOptions;

                book.UseDbFieldsFrom(dbBook);
                book.Monitored = monitored;

                // AddOptions carries SearchForNewBook, and it is the stored copy that
                // decides what gets searched: BookAddedService reads it back off the row
                // after the refresh. Adopting the existing row's options threw the
                // request to search away with them, so the book was monitored and then
                // quietly never looked for.
                if (requestedOptions != null)
                {
                    book.AddOptions = requestedOptions;
                }
            }

            // Remove any import list exclusions preventing addition
            _importListExclusionService.Delete(book.ForeignBookId);
            _importListExclusionService.Delete(book.AuthorMetadata.Value.ForeignAuthorId);

            // Note it's a manual addition so it's not deleted on next refresh
            book.AddOptions.AddType = BookAddType.Manual;
            book.Editions.Value.Single(x => x.Monitored).ManualAdd = true;

            // Add the author if necessary
            var dbAuthor = _authorService.FindById(book.AuthorMetadata.Value.ForeignAuthorId);
            if (dbAuthor == null)
            {
                var author = book.Author.Value;

                author.Metadata.Value.ForeignAuthorId = book.AuthorMetadata.Value.ForeignAuthorId;

                dbAuthor = _addAuthorService.AddAuthor(author, false);
            }

            // Wanted lists a book only when its author is monitored too, and an
            // author picked up by a refresh or a library scan is not. Adding a book
            // to be monitored, against an author nobody chose to follow, otherwise
            // leaves it monitored and invisible -- never searched, never listed.
            if (book.Monitored && !dbAuthor.Monitored)
            {
                _logger.Debug("Monitoring {0} so {1} can be searched for", dbAuthor, book);

                dbAuthor.Monitored = true;
                _authorService.UpdateAuthor(dbAuthor);
            }

            book.Author = dbAuthor;
            book.AuthorMetadataId = dbAuthor.AuthorMetadataId;
            _bookService.AddBook(book, doRefresh);

            return book;
        }

        public List<Book> AddBooks(List<Book> books, bool doRefresh = true)
        {
            var added = DateTime.UtcNow;
            var addedBooks = new List<Book>();

            foreach (var a in books)
            {
                a.Added = added;
                try
                {
                    addedBooks.Add(AddBook(a, doRefresh));
                }
                catch (Exception ex)
                {
                    // Could be a bad id from an import list
                    _logger.Error(ex, "Failed to import id: {0} - {1}", a.ForeignBookId, a.Title);
                }
            }

            return addedBooks;
        }

        private Book AddSkyhookData(Book newBook)
        {
            var editionId = newBook.Editions.Value.Single(x => x.Monitored).ForeignEditionId;

            Tuple<string, Book, List<AuthorMetadata>> tuple = null;
            try
            {
                tuple = _bookInfo.GetBookInfo(newBook.ForeignBookId);
            }
            catch (BookNotFoundException)
            {
                _logger.Error("Book with Foreign Id {0} was not found, it may have been removed from Goodreads.", newBook.ForeignBookId);

                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure("GoodreadsId", "A book with this ID was not found", newBook.ForeignBookId)
                                              });
            }

            newBook.UseMetadataFrom(tuple.Item2);
            newBook.Added = DateTime.UtcNow;

            newBook.Editions = tuple.Item2.Editions.Value;
            newBook.Editions.Value.ForEach(x => x.Monitored = false);
            newBook.Editions.Value.Single(x => x.ForeignEditionId == editionId).Monitored = true;

            var metadata = tuple.Item3.FirstOrDefault(x => x.ForeignAuthorId == tuple.Item1);
            newBook.AuthorMetadata = metadata;

            return newBook;
        }
    }
}

using System.Collections.Generic;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books
{
    public interface IResetAuthorService
    {
        Author ResetAuthor(int authorId, string foreignAuthorId);
    }

    /// <summary>
    /// Rebuilds an author from the metadata source, keeping their files.
    ///
    /// A normal refresh updates an author in place, so it can't recover from
    /// the metadata source having reorganised underneath it: books that were
    /// never returned stay missing, and an author whose foreign ID changed
    /// can't be refreshed at all. Deleting and re-adding is the only way to
    /// pick that up, which otherwise has to be done by hand.
    ///
    /// The replacement ID is chosen by the caller rather than resolved here.
    /// Searching by name can easily return a different person of the same name,
    /// and silently rebuilding an author as somebody else would be worse than
    /// leaving them stale.
    /// </summary>
    public class ResetAuthorService : IResetAuthorService
    {
        private readonly IAuthorService _authorService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ResetAuthorService(IAuthorService authorService,
                                  IAddAuthorService addAuthorService,
                                  IMediaFileService mediaFileService,
                                  IManageCommandQueue commandQueueManager,
                                  Logger logger)
        {
            _authorService = authorService;
            _addAuthorService = addAuthorService;
            _mediaFileService = mediaFileService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public Author ResetAuthor(int authorId, string foreignAuthorId)
        {
            var existing = _authorService.GetAuthor(authorId);

            if (foreignAuthorId.IsNullOrWhiteSpace())
            {
                foreignAuthorId = existing.Metadata.Value.ForeignAuthorId;
            }

            // Authors.AuthorMetadataId is unique, so re-adding under an ID
            // another author already holds fails on a constraint. Say so here
            // rather than after the original has been deleted.
            var conflict = _authorService.FindById(foreignAuthorId);

            if (conflict != null && conflict.Id != authorId)
            {
                throw new ValidationException(new List<ValidationFailure>
                {
                    new ("foreignAuthorId", $"'{conflict.Name}' is already in your library with this ID", foreignAuthorId)
                });
            }

            var fileCount = _mediaFileService.GetFilesByAuthor(authorId).Count;

            _logger.Info("Resetting author {0} to [{1}], keeping {2} file(s)", existing, foreignAuthorId, fileCount);

            var replacement = new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = foreignAuthorId },

                // Keep everything the user configured. Only the metadata is
                // being rebuilt.
                Path = existing.Path,
                RootFolderPath = existing.RootFolderPath,
                QualityProfileId = existing.QualityProfileId,
                MetadataProfileId = existing.MetadataProfileId,
                Monitored = existing.Monitored,
                MonitorNewItems = existing.MonitorNewItems,
                Tags = existing.Tags,

                AddOptions = new AddAuthorOptions
                {
                    // The rescan below re-monitors whatever is on disk. Adding
                    // the whole bibliography as monitored would queue searches
                    // for books the user never asked for.
                    Monitor = MonitorTypes.None,
                    BooksToMonitor = new List<string>(),
                    SearchForMissingBooks = false
                }
            };

            // Files are left on disk and re-imported by the rescan below.
            _authorService.DeleteAuthor(authorId, false, false);

            var added = _addAuthorService.AddAuthor(replacement);

            _commandQueueManager.Push(new RescanFoldersCommand(
                new List<string> { added.Path },
                FilterFilesType.Matched,
                false,
                new List<int> { added.Id }));

            return added;
        }
    }
}

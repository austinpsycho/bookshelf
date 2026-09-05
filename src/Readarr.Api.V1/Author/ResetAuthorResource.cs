namespace Readarr.Api.V1.Author
{
    /// <summary>
    /// Identifies which author to rebuild as. Chosen by the caller from search
    /// results rather than resolved server-side, so an author is never silently
    /// rebuilt as a different person of the same name.
    /// </summary>
    public class ResetAuthorResource
    {
        public string ForeignAuthorId { get; set; }
    }
}

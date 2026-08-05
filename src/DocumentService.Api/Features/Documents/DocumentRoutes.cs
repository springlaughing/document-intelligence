namespace DocumentService.Api.Features.Documents;

// Route names let one slice link to another slice's endpoint without referencing its
// types. RegisterDocument returns a Location header pointing at GetDocument, but it
// has no reason to know which class serves it.
public static class DocumentRoutes
{
    public const string GetDocumentById = "GetDocumentById";
}

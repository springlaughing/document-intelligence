namespace DocumentService.Api.Domain;

// The document lifecycle belongs to this service alone. It lives here rather than in
// the shared contracts project because only DocumentService.Api is allowed to add a
// value to it - the analysis service reports outcomes and never names a status.
public enum DocumentStatus
{
    Uploaded,
    Analyzing,
    Analyzed,
    Failed
}

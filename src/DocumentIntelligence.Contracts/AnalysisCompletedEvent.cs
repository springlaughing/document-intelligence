namespace DocumentIntelligence.Contracts;

// An outcome, not a lifecycle state: this says *what happened* in the analysis
// service. Mapping the outcome onto a document status is the API's own decision,
// so no DocumentStatus travels over the wire.
//
// EventId identifies this occurrence. Service Bus delivers at least once, so a
// consumer needs a stable way to recognise a message it has already applied - and
// the broker's own MessageId cannot serve, since it is assigned per send rather
// than per event.
public record AnalysisCompletedEvent(
    Guid EventId,
    Guid DocumentId,
    string Summary,
    string BlobReference // points to where detailed results are stored
);

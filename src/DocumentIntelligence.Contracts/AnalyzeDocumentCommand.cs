namespace DocumentIntelligence.Contracts;

// CommandId identifies this request to analyse, and stays the same however many times
// the broker delivers it. The analysis service has no store of its own, so it cannot
// remember what it has already done - instead it derives the id of the event it emits
// from this one, which makes a redelivered command produce a byte-identical result that
// the consumer's inbox recognises as a duplicate.
public record AnalyzeDocumentCommand(
    Guid CommandId,
    Guid DocumentId,
    string FileName
);

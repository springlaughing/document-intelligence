namespace DocumentIntelligence.Messaging;

// Thrown by a handler to say "this message can never succeed, do not retry it".
//
// The distinction matters because the two failures need opposite responses. A transient
// fault - the database was briefly unreachable - should be retried, and retrying costs
// nothing but time. A message referring to something that does not exist will fail
// identically forever, so retrying it burns the delivery count and then dead-letters it
// anyway, several minutes later and with a misleading reason attached.
//
// Handlers signal intent in their own terms; the transport decides how to express it.
public class UnprocessableMessageException : Exception
{
    public UnprocessableMessageException(string message) : base(message)
    {
    }

    public UnprocessableMessageException(string message, Exception inner) : base(message, inner)
    {
    }
}

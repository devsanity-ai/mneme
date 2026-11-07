using Mneme.Contracts;
using Mneme.Hosting.Profiles;
using Mneme.Ingest.Redaction;

namespace Mneme.Assistant;

/// <summary>
/// Self-describing metadata for <see cref="WaitingOnObservationPayload"/> so base
/// Mneme can (de)serialize, redact, full-text index, and summarize it without a
/// compile-time <c>switch</c> (Profile SDK / ADR-0005). Registered by
/// <see cref="PersonalAssistantProfile"/>.
/// </summary>
public sealed class WaitingOnObservationDescriptor : IPayloadDescriptor
{
    /// <inheritdoc/>
    public Type PayloadType => typeof(WaitingOnObservationPayload);

    /// <inheritdoc/>
    public string Discriminator => "WaitingOnObservationPayload";

    /// <inheritdoc/>
    public (EventPayload Payload, bool HadHits, int HitCount) Redact(EventPayload payload, IRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        var p = (WaitingOnObservationPayload)payload;

        // Free-text fields only (locked decision #11). WaitingOnId is a slug,
        // Status/Since/ExpectedBy/Confidence are structural — left intact. NOTE:
        // Counterparty is a person; the redactor scrubs secrets, not names, so
        // the host must have already pseudonymized it before ingest.
        var counterparty = redactor.Redact(p.Counterparty);
        var awaited = redactor.Redact(p.AwaitedItem);
        var had = counterparty.HadHits || awaited.HadHits;
        var hits = counterparty.Hits.Count + awaited.Hits.Count;

        string? sourceRef = p.SourceRef;
        if (sourceRef is not null)
        {
            var r = redactor.Redact(sourceRef);
            sourceRef = r.RedactedContent;
            had |= r.HadHits;
            hits += r.Hits.Count;
        }

        return (p with
        {
            Counterparty = counterparty.RedactedContent,
            AwaitedItem = awaited.RedactedContent,
            SourceRef = sourceRef,
        }, had, hits);
    }

    /// <inheritdoc/>
    public string ExtractText(EventPayload payload)
    {
        var p = (WaitingOnObservationPayload)payload;
        return string.Join(' ', new[] { p.Counterparty, p.AwaitedItem }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <inheritdoc/>
    public string Summarize(EventPayload payload)
    {
        var p = (WaitingOnObservationPayload)payload;
        return $"[{p.Status}] waiting on {p.Counterparty} for {p.AwaitedItem}";
    }
}

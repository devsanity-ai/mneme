using Mneme.Contracts;
using Mneme.Hosting.Profiles;
using Mneme.Ingest.Redaction;

namespace Mneme.Code;

internal sealed class CodeObservationDescriptor : IPayloadDescriptor
{
    public Type PayloadType => typeof(CodeObservationPayload);
    public string Discriminator => "CodeObservationPayload";

    public (EventPayload Payload, bool HadHits, int HitCount) Redact(EventPayload payload, IRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        var p = (CodeObservationPayload)payload;
        // Redacting an identity to a common marker would conflate unrelated scopes.
        RejectChangedIdentity(p.SourceRevision, redactor);
        RejectChangedIdentity(p.RelativePath, redactor);
        RejectChangedIdentity(p.Symbol, redactor);
        var content = redactor.Redact(p.Content);
        var evidence = p.EvidenceReference is null ? null : redactor.Redact(p.EvidenceReference);
        var redacted = new CodeObservationPayload(
            p.RepositoryId, p.SourceRevision, p.Kind, content.RedactedContent,
            p.RelativePath, p.Symbol, evidence?.RedactedContent);
        return (redacted, content.HadHits || evidence?.HadHits == true,
            content.Hits.Count + (evidence?.Hits.Count ?? 0));
    }

    private static void RejectChangedIdentity(string? value, IRedactor redactor)
    {
        if (value is null) return;
        var result = redactor.Redact(value);
        if (result.HadHits || !string.Equals(value, result.RedactedContent, StringComparison.Ordinal))
            throw new ArgumentException("Sensitive revision, path, or symbol is not a safe scope identifier.");
    }

    public string ExtractText(EventPayload payload)
    {
        var p = (CodeObservationPayload)payload;
        return string.Join(' ', new[] { p.Content, p.RelativePath, p.Symbol, p.EvidenceReference }
            .Where(value => value is not null));
    }

    public string Summarize(EventPayload payload)
    {
        var p = (CodeObservationPayload)payload;
        return $"Advisory {p.Kind}: {p.Content}";
    }
}

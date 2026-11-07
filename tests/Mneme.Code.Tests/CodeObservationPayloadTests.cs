using System.Text.Json;
using System.Text.Json.Nodes;
using Mneme.Contracts;
using Mneme.Hosting.Profiles;
using Mneme.Ingest.Redaction;
using Mneme.Storage;

namespace Mneme.Code.Tests;

public sealed class CodeObservationPayloadTests
{
    private readonly IPayloadDescriptor _descriptor = new CodeProfile().PayloadDescriptors.Single();

    public CodeObservationPayloadTests() => PayloadDescriptorRegistry.Register(_descriptor);

    [Theory]
    [InlineData(CodeObservationKind.Convention)]
    [InlineData(CodeObservationKind.ArchitectureObservation)]
    [InlineData(CodeObservationKind.FixLesson)]
    public void Immutable_payload_roundtrips_as_advisory_evidence(CodeObservationKind kind)
    {
        var original = CodeTestHost.Payload(kind: kind, path: "src/Cache.cs",
            symbol: "Cache.Read", evidence: "host-observation-17");
        var json = EventSerialization.SerializePayload(original);

        Assert.Contains("\"$type\":\"CodeObservationPayload\"", json);
        var restored = Assert.IsType<CodeObservationPayload>(EventSerialization.DeserializePayload(json));
        Assert.Equal(original, restored);
        Assert.Equal(EpistemicCategory.Evidence, restored.Category);
        Assert.All(typeof(CodeObservationPayload).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Equal(new[] { "Convention", "ArchitectureObservation", "FixLesson" },
            Enum.GetNames<CodeObservationKind>());
    }

    [Fact]
    public void Full_sha256_revision_roundtrips_and_missing_evidence_stays_unknown()
    {
        var original = CodeTestHost.Payload(revision: new string('a', 64));
        var restored = Assert.IsType<CodeObservationPayload>(
            EventSerialization.DeserializePayload(EventSerialization.SerializePayload(original)));
        Assert.Equal(original, restored);
        Assert.Null(restored.EvidenceReference);
        Assert.Null(restored.RelativePath);
        Assert.Null(restored.Symbol);
    }

    [Fact]
    public void Descriptor_text_and_summary_are_advisory_not_decision_authority()
    {
        var payload = CodeTestHost.Payload(kind: CodeObservationKind.ArchitectureObservation,
            content: "An observer suggests queue-based delivery.", path: "src/Queue.cs",
            symbol: "Queue.Deliver", evidence: "unverified-host-reference");

        Assert.Equal(typeof(CodeObservationPayload), _descriptor.PayloadType);
        Assert.Equal("CodeObservationPayload", _descriptor.Discriminator);
        Assert.Equal("Advisory ArchitectureObservation: An observer suggests queue-based delivery.",
            _descriptor.Summarize(payload));
        foreach (var text in new[] { payload.Content, payload.RelativePath, payload.Symbol, payload.EvidenceReference })
            Assert.Contains(text!, _descriptor.ExtractText(payload));
    }

    [Fact]
    public void Unknown_payload_discriminator_is_still_rejected()
    {
        Assert.Throws<JsonException>(() =>
            EventSerialization.DeserializePayload("""{"$type":"UnregisteredCodePayload"}"""));
    }

    [Theory]
    [InlineData("repositoryId", "null")]
    [InlineData("repositoryId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("repositoryId", "\"https://repository.invalid\"")]
    [InlineData("sourceRevision", "null")]
    [InlineData("sourceRevision", "\"HEAD\"")]
    [InlineData("content", "null")]
    [InlineData("content", "\" \"")]
    [InlineData("kind", "-1")]
    [InlineData("kind", "99")]
    [InlineData("kind", "null")]
    [InlineData("kind", "\"Convention\"")]
    [InlineData("category", "2")]
    [InlineData("relativePath", "\"../Cache.cs\"")]
    [InlineData("symbol", "\"Cache.Read\"")]
    [InlineData("evidenceReference", "\"\"")]
    [InlineData("confidence", "0.9")]
    [InlineData("unexpectedSourceBody", "\"must not silently drop input\"")]
    public void Malformed_wire_values_are_rejected(string property, string jsonValue)
    {
        var json = JsonNode.Parse(EventSerialization.SerializePayload(CodeTestHost.Payload()))!.AsObject();
        json[property] = JsonNode.Parse(jsonValue);

        var exception = Record.Exception(() => EventSerialization.DeserializePayload(json.ToJsonString()));
        Assert.True(exception is JsonException or ArgumentException,
            $"Expected malformed {property} to be rejected, got {exception?.GetType().Name ?? "no error"}.");
    }

    [Theory]
    [InlineData("repositoryId")]
    [InlineData("sourceRevision")]
    [InlineData("kind")]
    [InlineData("content")]
    public void Missing_required_wire_fields_are_rejected(string property)
    {
        var json = JsonNode.Parse(EventSerialization.SerializePayload(CodeTestHost.Payload()))!.AsObject();
        Assert.True(json.Remove(property));
        var exception = Record.Exception(() => EventSerialization.DeserializePayload(json.ToJsonString()));
        Assert.True(exception is JsonException or ArgumentException,
            $"Expected missing {property} to be rejected, got {exception?.GetType().Name ?? "no error"}.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("HEAD")]
    [InlineData("1111111")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("1111111111111111111111111111111111111111\n")]
    public void Unknown_or_noncanonical_revisions_are_rejected(string? revision)
    {
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(revision: revision!));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Query(revision: revision!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../Cache.cs")]
    [InlineData("src/../Cache.cs")]
    [InlineData("/src/Cache.cs")]
    [InlineData("C:/Cache.cs")]
    [InlineData("C:\\Cache.cs")]
    [InlineData("src\\Cache.cs")]
    [InlineData("src//Cache.cs")]
    [InlineData("./Cache.cs")]
    [InlineData("src/Cache.cs/")]
    [InlineData("src/Cache.cs.")]
    [InlineData("src/ Cache.cs")]
    [InlineData("src/Cache.cs ")]
    [InlineData("src/*.cs")]
    [InlineData("src/Cache?.cs")]
    [InlineData("src/\nCache.cs")]
    public void Unsafe_or_ambiguous_paths_are_rejected_without_normalization(string path)
    {
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(path: path));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Query(path: path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" Cache.Read")]
    [InlineData("Cache.Read ")]
    [InlineData("Cache.\nRead")]
    public void Invalid_symbols_are_rejected(string symbol)
    {
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(path: "src/Cache.cs", symbol: symbol));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Query(path: "src/Cache.cs", symbol: symbol));
    }

    [Fact]
    public void Empty_repository_invalid_kind_and_oversized_fields_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(repository: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Query(repository: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => CodeTestHost.Payload(kind: (CodeObservationKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => CodeTestHost.Query(kind: (CodeObservationKind)99));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(content: null!));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(content: "\0"));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(content: new string('a', 2049)));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(evidence: new string('a', 1025)));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(path: new string('a', 513)));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(path: "Cache.cs", symbol: new string('a', 257)));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(symbol: "Cache.Read"));
        Assert.Throws<ArgumentException>(() => CodeTestHost.Query(symbol: "Cache.Read"));
    }

    [Fact]
    public void Invalid_unicode_is_rejected_instead_of_collapsing_scope_identifiers_on_persistence()
    {
        foreach (var invalid in new[] { "\ud800", "\udc00", "before\ud800after" })
        {
            Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(content: invalid));
            Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(evidence: invalid));
            Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(path: $"src/{invalid}.cs"));
            Assert.Throws<ArgumentException>(() => CodeTestHost.Payload(path: "src/Cache.cs", symbol: invalid));
            Assert.Throws<ArgumentException>(() => CodeTestHost.Query(path: $"src/{invalid}.cs"));
            Assert.Throws<ArgumentException>(() => CodeTestHost.Query(path: "src/Cache.cs", symbol: invalid));
        }
        var valid = CodeTestHost.Payload(path: "src/\U0001f9e0.cs", content: "A Unicode \U0001f9e0 observation.");
        Assert.Equal(valid, EventSerialization.DeserializePayload(EventSerialization.SerializePayload(valid)));
    }

    [Fact]
    public void Descriptor_visits_every_string_and_preserves_the_original()
    {
        var payload = CodeTestHost.Payload(content: "sensitive-content", path: "src/Cache.cs",
            symbol: "Cache.Read", evidence: "sensitive-evidence");
        var redactor = new TestRedactor(value => value.StartsWith("sensitive", StringComparison.Ordinal)
            ? new RedactionResult("[REDACTED]", [new RedactionHit("fixture", "[REDACTED]", 0, value.Length)])
            : new RedactionResult(value, []));

        var (result, hadHits, hits) = _descriptor.Redact(payload, redactor);
        var redacted = Assert.IsType<CodeObservationPayload>(result);
        Assert.True(hadHits);
        Assert.Equal(2, hits);
        Assert.Equal("[REDACTED]", redacted.Content);
        Assert.Equal("[REDACTED]", redacted.EvidenceReference);
        Assert.Equal("sensitive-content", payload.Content);
        Assert.Equal("sensitive-evidence", payload.EvidenceReference);
        Assert.Equal(new[]
        {
            payload.SourceRevision, payload.RelativePath, payload.Symbol, payload.Content, payload.EvidenceReference,
        }, redactor.Inputs);
    }
}

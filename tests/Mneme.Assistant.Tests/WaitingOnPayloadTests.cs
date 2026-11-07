using Mneme.Contracts;
using Mneme.Hosting.Profiles;
using Mneme.Ingest.Redaction;
using Mneme.Storage;

namespace Mneme.Assistant.Tests;

/// <summary>
/// Host-free unit proofs of the <see cref="WaitingOnObservationPayload"/> contract
/// and its <see cref="WaitingOnObservationDescriptor"/>: shared-serializer
/// round-trip via the registry, and the descriptor's redaction / text / summary
/// seams.
/// </summary>
public sealed class WaitingOnPayloadTests
{
    public WaitingOnPayloadTests()
        => PayloadDescriptorRegistry.Register(new WaitingOnObservationDescriptor());

    [Fact]
    public void Defaults_are_waiting_status_full_confidence_and_hypothesis_category()
    {
        var p = new WaitingOnObservationPayload("wait:x", "alice", "the thing",
            Since: DateTimeOffset.UnixEpoch);
        Assert.Equal(WaitingOnStatus.Waiting, p.Status);
        Assert.Equal(1.0, p.Confidence);
        Assert.Null(p.ExpectedBy);
        Assert.Equal(EpistemicCategory.Hypothesis, p.Category);
    }

    [Fact]
    public void Roundtrips_through_the_shared_serializer_with_its_discriminator()
    {
        var original = new WaitingOnObservationPayload(
            "wait:budget", "alice", "budget sign-off",
            Since: new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero),
            Status: WaitingOnStatus.Waiting,
            ExpectedBy: new DateTimeOffset(2026, 1, 9, 17, 0, 0, TimeSpan.Zero),
            Confidence: 0.8,
            SourceRef: "thread:abc");

        var json = EventSerialization.SerializePayload(original);
        Assert.Contains("\"$type\":\"WaitingOnObservationPayload\"", json);

        var back = Assert.IsType<WaitingOnObservationPayload>(EventSerialization.DeserializePayload(json));
        Assert.Equal(original, back);
    }

    [Fact]
    public void Descriptor_summary_reads_status_counterparty_and_item()
    {
        var descriptor = new WaitingOnObservationDescriptor();
        var p = new WaitingOnObservationPayload("wait:x", "alice", "budget sign-off",
            Since: DateTimeOffset.UnixEpoch);
        Assert.Equal("[Waiting] waiting on alice for budget sign-off", descriptor.Summarize(p));
    }

    [Fact]
    public void Descriptor_redacts_counterparty_awaited_item_and_source_ref()
    {
        var descriptor = new WaitingOnObservationDescriptor();
        var p = new WaitingOnObservationPayload(
            WaitingOnId: "wait:x",
            Counterparty: "supersecret1",
            AwaitedItem: "token supersecret2 here",
            Since: DateTimeOffset.UnixEpoch,
            SourceRef: "supersecret3");

        var (payload, had, hits) = descriptor.Redact(p, new MaskWordRedactor("supersecret"));
        var r = Assert.IsType<WaitingOnObservationPayload>(payload);

        Assert.True(had);
        Assert.Equal(3, hits);
        Assert.DoesNotContain("supersecret", r.Counterparty);
        Assert.DoesNotContain("supersecret", r.AwaitedItem);
        Assert.DoesNotContain("supersecret", r.SourceRef);
        // WaitingOnId (slug) and structural fields are left intact.
        Assert.Equal("wait:x", r.WaitingOnId);
    }

    private sealed class MaskWordRedactor : IRedactor
    {
        private readonly string _marker;
        public MaskWordRedactor(string marker) => _marker = marker;

        public RedactionResult Redact(string content)
        {
            if (string.IsNullOrEmpty(content)) return new RedactionResult(content, Array.Empty<RedactionHit>());
            var hits = new List<RedactionHit>();
            var words = content.Split(' ');
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].StartsWith(_marker, StringComparison.Ordinal))
                {
                    hits.Add(new RedactionHit("secret", "[REDACTED]", i, words[i].Length));
                    words[i] = "[REDACTED]";
                }
            }
            return new RedactionResult(string.Join(' ', words), hits);
        }
    }
}

using Mneme.Contracts;

namespace Mneme.Code;

/// <summary>
/// Independently attributed advisory context. Even review-approved observations
/// are not accepted decisions. RecordedAt is the core WAL ingest clock, distinct
/// from ValidAt and Knowledge.SourceRevision.
/// </summary>
public sealed record CodeObservation(
    EventId EventId,
    WorkstreamId Workstream,
    CodeObservationPayload Knowledge,
    PrincipalId Principal,
    CaptureSourceId Source,
    DateTimeOffset ValidAt,
    DateTimeOffset RecordedAt);

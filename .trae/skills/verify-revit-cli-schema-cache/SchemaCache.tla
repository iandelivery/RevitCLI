---------------------------- MODULE SchemaCache ----------------------------
EXTENDS Naturals

VARIABLES server, diskSchema, diskEtag, fresh, phase, served, servedVia, servedStale

None == "none"
S1 == "v1"
S2 == "v2"

EtagOf(s) == IF s = S1 THEN "etag-v1" ELSE IF s = S2 THEN "etag-v2" ELSE None

Rank(s) == IF s = S2 THEN 2 ELSE IF s = S1 THEN 1 ELSE IF s = "etag-v2" THEN 2 ELSE IF s = "etag-v1" THEN 1 ELSE 0

Idle == "idle"
Asking == "asking"
Got200 == "got200"
WroteEtag == "wroteEtag"

Init ==
  /\ server = S1
  /\ diskSchema = None
  /\ diskEtag = None
  /\ fresh = FALSE
  /\ phase = Idle
  /\ served = None
  /\ servedVia = None
  /\ servedStale = FALSE

vars == <<server, diskSchema, diskEtag, fresh, phase, served, servedVia, servedStale>>

BridgeUpgrade ==
  /\ server = S1
  /\ server' = S2
  /\ UNCHANGED <<diskSchema, diskEtag, fresh, phase, served, servedVia, servedStale>>

TimePasses ==
  /\ phase = Idle
  /\ fresh
  /\ fresh' = FALSE
  /\ UNCHANGED <<server, diskSchema, diskEtag, phase, served, servedVia, servedStale>>

ServeTtl ==
  /\ phase = Idle
  /\ diskSchema # None
  /\ fresh
  /\ served' = diskSchema
  /\ servedVia' = "ttl"
  /\ servedStale' = (diskSchema # server)
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh, phase>>

StartRequest ==
  /\ phase = Idle
  /\ phase' = Asking
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh, served, servedVia, servedStale>>

Answer200 ==
  /\ phase = Asking
  /\ diskEtag # EtagOf(server)
  /\ served' = server
  /\ servedVia' = "200"
  /\ servedStale' = FALSE
  /\ phase' = Got200
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh>>

Answer304 ==
  /\ phase = Asking
  /\ diskEtag = EtagOf(server)
  /\ IF diskSchema = None THEN UNCHANGED fresh ELSE fresh' = TRUE
  /\ served' = diskSchema
  /\ servedVia' = "304"
  /\ servedStale' = (diskSchema # server)
  /\ phase' = Idle
  /\ UNCHANGED <<server, diskSchema, diskEtag>>

AnswerUnreachable ==
  /\ phase = Asking
  /\ served' = diskSchema
  /\ servedVia' = "unreachable"
  /\ servedStale' = (diskSchema # server)
  /\ phase' = Idle
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh>>

SaveEtagOk ==
  /\ phase = Got200
  /\ diskEtag' = EtagOf(served)
  /\ phase' = WroteEtag
  /\ UNCHANGED <<server, diskSchema, fresh, served, servedVia, servedStale>>

SaveEtagFail ==
  /\ phase = Got200
  /\ phase' = WroteEtag
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh, served, servedVia, servedStale>>

SaveSchemaOk ==
  /\ phase = WroteEtag
  /\ diskSchema' = served
  /\ fresh' = TRUE
  /\ phase' = Idle
  /\ UNCHANGED <<server, diskEtag, served, servedVia, servedStale>>

SaveSchemaFail ==
  /\ phase = WroteEtag
  /\ phase' = Idle
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh, served, servedVia, servedStale>>

ProcessDies ==
  /\ phase \in {Got200, WroteEtag}
  /\ phase' = Idle
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh, served, servedVia, servedStale>>

Next ==
  \/ BridgeUpgrade
  \/ TimePasses
  \/ ServeTtl
  \/ StartRequest
  \/ Answer200
  \/ Answer304
  \/ AnswerUnreachable
  \/ SaveEtagOk
  \/ SaveEtagFail
  \/ SaveSchemaOk
  \/ SaveSchemaFail
  \/ ProcessDies

StampNeverAheadOfSchema == phase # Idle \/ Rank(diskEtag) =< Rank(diskSchema)

NotModifiedIsTruthful == servedVia # "304" \/ ~servedStale

CatchesUpAfterUpgrade == [](server = S2 => <><>(served = S2))

Fair ==
  /\ SF_vars(ServeTtl \/ StartRequest)
  /\ SF_vars(TimePasses)
  /\ SF_vars(Answer200 \/ Answer304)
  /\ SF_vars(SaveEtagOk \/ SaveEtagFail \/ SaveSchemaOk \/ SaveSchemaFail \/ ProcessDies)

Spec == Init /\ [][Next]_vars /\ Fair
=============================================================================

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
WroteSchema == "wroteSchema"

LegacyDisk ==
  \* A disk the pre-fix client left behind: it wrote the stamp before the list,
  \* so a failed list write left a stamp with no list. Reachable only by
  \* upgrading from that client; the current client cannot produce it.
  /\ diskEtag = EtagOf(server)
  /\ diskSchema = None

Init ==
  /\ server = S1
  /\ fresh = FALSE
  /\ phase = Idle
  /\ served = None
  /\ servedVia = None
  /\ servedStale = FALSE
  /\ \/ /\ diskSchema = None
        /\ diskEtag = None
     \/ LegacyDisk

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

SentEtag == IF diskSchema = None THEN None ELSE diskEtag

Answer200 ==
  /\ phase = Asking
  /\ SentEtag # EtagOf(server)
  /\ served' = server
  /\ servedVia' = "200"
  /\ servedStale' = FALSE
  /\ phase' = Got200
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh>>

Answer304 ==
  /\ phase = Asking
  /\ SentEtag = EtagOf(server)
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

SaveSchemaOk ==
  /\ phase = Got200
  /\ diskSchema' = served
  /\ fresh' = TRUE
  /\ phase' = WroteSchema
  /\ UNCHANGED <<server, diskEtag, served, servedVia, servedStale>>

SaveSchemaFail ==
  /\ phase = Got200
  /\ phase' = Idle
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh, served, servedVia, servedStale>>

SaveEtagOk ==
  /\ phase = WroteSchema
  /\ diskEtag' = EtagOf(served)
  /\ phase' = Idle
  /\ UNCHANGED <<server, diskSchema, fresh, served, servedVia, servedStale>>

SaveEtagFail ==
  /\ phase = WroteSchema
  /\ phase' = Idle
  /\ UNCHANGED <<server, diskSchema, diskEtag, fresh, served, servedVia, servedStale>>

ProcessDies ==
  /\ phase \in {Got200, WroteSchema}
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
  \/ SaveSchemaOk
  \/ SaveSchemaFail
  \/ SaveEtagOk
  \/ SaveEtagFail
  \/ ProcessDies

\* The client never leaves a stamp that describes a newer bridge than the
\* list it saved. A stamp with no list is the legacy residue the pre-fix
\* client left behind, not something the current client writes.
StampNeverAheadOfSchema == phase # Idle \/ diskSchema = None \/ Rank(diskEtag) =< Rank(diskSchema)

\* The client never leaves a stamp that is ahead of the list it saved. The
\* legacy initial disk may violate this; every state the current client
\* reaches through Next must satisfy it.
ClientKeepsStampInStep == [][StampNeverAheadOfSchema]_vars

NotModifiedIsTruthful == servedVia # "304" \/ ~servedStale

CatchesUpAfterUpgrade == [](server = S2 => <><>(served = S2))

Fair ==
  /\ SF_vars(ServeTtl \/ StartRequest)
  /\ SF_vars(TimePasses)
  /\ SF_vars(Answer200 \/ Answer304)
  /\ SF_vars(SaveSchemaOk \/ SaveSchemaFail \/ SaveEtagOk \/ SaveEtagFail \/ ProcessDies)

Spec == Init /\ [][Next]_vars /\ Fair
=============================================================================

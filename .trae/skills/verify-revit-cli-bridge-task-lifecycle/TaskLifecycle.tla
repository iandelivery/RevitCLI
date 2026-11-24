---------------------------- MODULE TaskLifecycle ----------------------------
EXTENDS Naturals, Sequences

VARIABLES status, result, tcs, winner, outcome, revitPc, httpPc, stream

None == "none"
Pending == "pending"
Running == "running"
Completed == "completed"
Failed == "failed"
Timeout == "timeout"
Terminal == {Completed, Failed, Timeout}

Ok == "ok"
Fail == "fail"
Tmo == "timeout"

EndOf(o) == IF o = Ok THEN Completed ELSE Failed
FrameOf(o) == IF o = Ok THEN "completed" ELSE "failed"

Init ==
  /\ status = Pending
  /\ result = None
  /\ tcs = None
  /\ winner = None
  /\ outcome = None
  /\ revitPc = "queued"
  /\ httpPc = "waiting"
  /\ stream = <<>>

vars == <<status, result, tcs, winner, outcome, revitPc, httpPc, stream>>

RevitStartCheck ==
  /\ revitPc = "queued"
  /\ revitPc' = IF status \in Terminal THEN "exit" ELSE "startChecked"
  /\ UNCHANGED <<status, result, tcs, winner, outcome, httpPc, stream>>

RevitStartWrite ==
  /\ revitPc = "startChecked"
  /\ status' = Running
  /\ revitPc' = "running"
  /\ UNCHANGED <<result, tcs, winner, outcome, httpPc, stream>>

RevitFinishes ==
  /\ revitPc = "running"
  /\ outcome' \in {Ok, Fail}
  /\ revitPc' = "done"
  /\ UNCHANGED <<status, result, tcs, winner, httpPc, stream>>

RevitEndCheck ==
  /\ revitPc = "done"
  /\ revitPc' = IF status \in Terminal THEN "exit" ELSE "endChecked"
  /\ UNCHANGED <<status, result, tcs, winner, outcome, httpPc, stream>>

RevitEndWrite ==
  /\ revitPc = "endChecked"
  /\ status' = EndOf(outcome)
  /\ winner' = IF winner = None THEN "revit" ELSE winner
  /\ revitPc' = "statusSet"
  /\ UNCHANGED <<result, tcs, outcome, httpPc, stream>>

RevitResultWrite ==
  /\ revitPc = "statusSet"
  /\ result' = outcome
  /\ revitPc' = "resultSet"
  /\ UNCHANGED <<status, tcs, winner, outcome, httpPc, stream>>

RevitBroadcast ==
  /\ revitPc = "resultSet"
  /\ stream' = Append(stream, FrameOf(outcome))
  /\ revitPc' = "broadcast"
  /\ UNCHANGED <<status, result, tcs, winner, outcome, httpPc>>

RevitTcsWrite ==
  /\ revitPc = "broadcast"
  /\ tcs' = IF tcs = None THEN outcome ELSE tcs
  /\ revitPc' = "exit"
  /\ UNCHANGED <<status, result, winner, outcome, httpPc, stream>>

HttpWakes ==
  /\ httpPc = "waiting"
  /\ httpPc' = "woke"
  /\ UNCHANGED <<status, result, tcs, winner, outcome, revitPc, stream>>

HttpEndCheck ==
  /\ httpPc = "woke"
  /\ httpPc' = IF status \in Terminal THEN "finalPending" ELSE "endChecked"
  /\ UNCHANGED <<status, result, tcs, winner, outcome, revitPc, stream>>

HttpEndWrite ==
  /\ httpPc = "endChecked"
  /\ status' = Timeout
  /\ winner' = IF winner = None THEN "http" ELSE winner
  /\ httpPc' = "statusSet"
  /\ UNCHANGED <<result, tcs, outcome, revitPc, stream>>

HttpResultWrite ==
  /\ httpPc = "statusSet"
  /\ result' = Tmo
  /\ httpPc' = "resultSet"
  /\ UNCHANGED <<status, tcs, winner, outcome, revitPc, stream>>

HttpBroadcast ==
  /\ httpPc = "resultSet"
  /\ stream' = Append(stream, "failed")
  /\ httpPc' = "broadcast"
  /\ UNCHANGED <<status, result, tcs, winner, outcome, revitPc>>

HttpTcsWrite ==
  /\ httpPc = "broadcast"
  /\ tcs' = IF tcs = None THEN Tmo ELSE tcs
  /\ httpPc' = "finalPending"
  /\ UNCHANGED <<status, result, winner, outcome, revitPc, stream>>

HttpFinalFrame ==
  /\ httpPc = "finalPending"
  /\ httpPc' = "exit"
  /\ UNCHANGED <<status, result, tcs, winner, outcome, revitPc, stream>>

Done ==
  /\ revitPc = "exit"
  /\ httpPc = "exit"
  /\ UNCHANGED vars

Next ==
  \/ RevitStartCheck
  \/ RevitStartWrite
  \/ RevitFinishes
  \/ RevitEndCheck
  \/ RevitEndWrite
  \/ RevitResultWrite
  \/ RevitBroadcast
  \/ RevitTcsWrite
  \/ HttpWakes
  \/ HttpEndCheck
  \/ HttpEndWrite
  \/ HttpResultWrite
  \/ HttpBroadcast
  \/ HttpTcsWrite
  \/ HttpFinalFrame
  \/ Done

FirstTerminalWins ==
  /\ (winner = None) <=> ~(status \in Terminal)
  /\ winner = "revit" => /\ status = EndOf(outcome)
                          /\ result \in {None, outcome}
                          /\ tcs \in {None, outcome}
  /\ winner = "http"  => /\ status = Timeout
                          /\ result \in {None, Tmo}
                          /\ tcs \in {None, Tmo}

EndFrames(s) == { i \in 1..Len(s) : s[i] \in {"completed", "failed"} }

OneOutcomeOnTheWire ==
  \A i \in EndFrames(stream), j \in EndFrames(stream) :
    i =< j => stream[j] = stream[i]

Spec == Init /\ [][Next]_vars
=============================================================================

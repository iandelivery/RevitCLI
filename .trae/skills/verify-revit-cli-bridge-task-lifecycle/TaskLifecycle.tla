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

RevitStart ==
  /\ revitPc = "queued"
  /\ IF status \in Terminal
       THEN /\ revitPc' = "exit"
            /\ UNCHANGED <<status, result, tcs, winner>>
       ELSE /\ status' = Running
            /\ revitPc' = "running"
            /\ UNCHANGED <<result, tcs, winner>>
  /\ UNCHANGED <<outcome, httpPc>>
  /\ IF status \in Terminal THEN UNCHANGED stream ELSE stream' = Append(stream, "progress")

RevitFinishes ==
  /\ revitPc = "running"
  /\ outcome' \in {Ok, Fail}
  /\ revitPc' = "done"
  /\ UNCHANGED <<status, result, tcs, winner, httpPc, stream>>

RevitEnd ==
  /\ revitPc = "done"
  /\ IF status \in Terminal
       THEN /\ revitPc' = "exit"
            /\ UNCHANGED <<status, result, tcs, winner, outcome, httpPc, stream>>
       ELSE /\ status' = EndOf(outcome)
            /\ winner' = IF winner = None THEN "revit" ELSE winner
            /\ stream' = Append(stream, FrameOf(outcome))
            /\ revitPc' = "statusSet"
            /\ UNCHANGED <<result, tcs, outcome, httpPc>>

RevitResultWrite ==
  /\ revitPc = "statusSet"
  /\ result' = outcome
  /\ revitPc' = "resultSet"
  /\ UNCHANGED <<status, tcs, winner, outcome, httpPc, stream>>

RevitTcsWrite ==
  /\ revitPc = "resultSet"
  /\ tcs' = IF tcs = None THEN outcome ELSE tcs
  /\ revitPc' = "exit"
  /\ UNCHANGED <<status, result, winner, outcome, httpPc, stream>>

HttpWakes ==
  /\ httpPc = "waiting"
  /\ httpPc' = "woke"
  /\ UNCHANGED <<status, result, tcs, winner, outcome, revitPc, stream>>

HttpEnd ==
  /\ httpPc = "woke"
  /\ IF status \in Terminal
       THEN /\ httpPc' = "finalPending"
            /\ UNCHANGED <<status, result, tcs, winner, outcome, revitPc, stream>>
       ELSE /\ status' = Timeout
            /\ winner' = IF winner = None THEN "http" ELSE winner
            /\ stream' = Append(stream, "failed")
            /\ httpPc' = "statusSet"
            /\ UNCHANGED <<result, tcs, outcome, revitPc>>

HttpResultWrite ==
  /\ httpPc = "statusSet"
  /\ result' = Tmo
  /\ httpPc' = "resultSet"
  /\ UNCHANGED <<status, tcs, winner, outcome, revitPc, stream>>

HttpTcsWrite ==
  /\ httpPc = "resultSet"
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
  \/ RevitStart
  \/ RevitFinishes
  \/ RevitEnd
  \/ RevitResultWrite
  \/ RevitTcsWrite
  \/ HttpWakes
  \/ HttpEnd
  \/ HttpResultWrite
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

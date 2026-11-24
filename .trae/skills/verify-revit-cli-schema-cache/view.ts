const bridge = (v) => (v.server === "v2" ? "Bridge runs v2" : "Bridge runs v1");

const list = (s) => (s === "v1" ? "v1 list" : s === "v2" ? "v2 list" : "no list");
const stamp = (e) => (e === "etag-v1" ? "v1 stamp" : e === "etag-v2" ? "v2 stamp" : "no stamp");

const disk = (v) => `Disk: ${list(v.diskSchema)} + ${stamp(v.diskEtag)}`;

const client = (v) => {
  if (v.phase === "asking") return "client asks the bridge";
  if (v.phase === "got200") return `client got the ${list(v.served)} + its stamp, about to write the stamp`;
  if (v.phase === "wroteEtag") return `client is about to write the ${list(v.served)}`;
  if (v.servedVia === "304") {
    return v.servedStale
      ? `client served the disk copy (${list(v.served)}) after "not modified" - not the bridge's list`
      : `client served the disk copy (${list(v.served)}) after "not modified"`;
  }
  return "client idle";
};

export const state = (v) => `${bridge(v)} | ${disk(v)} | ${client(v)}`;

export const step = (action, from, to) => {
  if (action === "ServeTtl" || action === "TimePasses") return null;
  if (action === "BridgeUpgrade" && from.phase !== "idle") return null;
  if (action === "BridgeUpgrade") return "the bridge upgrades to v2";
  if (action === "StartRequest") return "you run a command (or commands --refresh)";
  if (action === "Answer200") return `the bridge answers 200 with the ${list(to.served)} + its stamp`;
  if (action === "Answer304") return 'the bridge answers 304 "not modified"';
  if (action === "AnswerUnreachable") return "the bridge is unreachable";
  if (action === "SaveEtagOk") return "the client writes the stamp file";
  if (action === "SaveEtagFail") return "writing the stamp file fails";
  if (action === "SaveSchemaOk") return "the client writes the list file";
  if (action === "SaveSchemaFail") return "writing the list file fails [disk full or file locked]";
  if (action === "ProcessDies") return "the client exits before saving";
  return action;
};

export const group = (v) => (v.server === "v2" ? "After the upgrade" : "Before the upgrade");
export const colors = { "Before the upgrade": "blue", "After the upgrade": "purple" };
export const title = "revit-cli schema cache: what the client serves after a bridge upgrade";

import { Collector } from "./collector.mjs";
const $ = id => document.getElementById(id);
const api = globalThis.overwolf;
let heartbeatTimer = null;
let heartbeatWriteTimer = null;
const collector = api ? new Collector(api, status => {
  $("status").textContent = JSON.stringify(status, null, 2);
  if (heartbeatTimer !== null) scheduleHeartbeatWrite();
}) : null;
$("availability").textContent = api ? "Pronto. Clique em iniciar; depois abra o Warframe." :
  "Abra este pacote como extensão local no Overwolf. O navegador comum não oferece GEP.";
const localAppData = api?.io?.paths?.localAppData;
if (localAppData) {
  $("folder").value = `${localAppData.replace(/[\\/]+$/, "")}\\MyFrame\\captures`;
  $("folder-hint").textContent = "Inbox My Frame sugerida automaticamente; confirme antes de exportar.";
}
if (!api) for (const button of document.querySelectorAll("button")) button.disabled = true;
$("start").onclick = async () => {
  collector.start();
  await writeHeartbeat();
  if (heartbeatTimer !== null) clearInterval(heartbeatTimer);
  heartbeatTimer = setInterval(() => { void writeHeartbeat(true); }, 15 * 60 * 1000);
};
$("stop").onclick = () => {
  if (heartbeatTimer !== null) { clearInterval(heartbeatTimer); heartbeatTimer = null; }
  if (heartbeatWriteTimer !== null) { clearTimeout(heartbeatWriteTimer); heartbeatWriteTimer = null; }
  collector.stop(); $("consent").checked = false;
  void writeHeartbeat(true, "stopped");
};
addEventListener("unload", () => {
  if (heartbeatTimer !== null) clearInterval(heartbeatTimer);
  if (heartbeatWriteTimer !== null) clearTimeout(heartbeatWriteTimer);
  collector?.stop();
});

function write(name, text) {
  const folder = $("folder").value.trim().replace(/[\\/]+$/, "");
  if (!/^[a-zA-Z]:[\\/]/.test(folder) || /[<>|"*?]/.test(folder) || folder.split(/[\\/]/).includes(".."))
    return Promise.reject(new Error("OUTPUT_FOLDER_REQUIRED"));
  return new Promise((resolve, reject) => {
    api.io.writeFileContents(`${folder}\\${name}`, text, api.io.enums.eEncoding.UTF8, false,
      result => result?.success ? resolve() : reject(new Error("WRITE_FAILED")));
  });
}
async function writeHeartbeat(quiet = false, heartbeatState = "started") {
  try {
    const report = collector?.report();
    await write("collector-status.json", JSON.stringify({
      schemaVersion: 1,
      kind: "my-frame-collector",
      state: heartbeatState,
      timestampUtc: new Date().toISOString(),
      collectorState: report?.state ?? "notStarted",
      supportedFeatures: report?.supportedFeatures ?? [],
      eventCounts: report?.eventCounts ?? {},
      lastEventFeature: report?.lastEventFeature ?? null,
      lastEventAt: report?.lastEventAt ?? null,
      inventoryState: report?.inventory?.rootObject === true ? "observedUnverified" : "notObserved"
    }));
    if (!quiet) $("export-status").textContent = "Sessão registrada na inbox; agora abra o Warframe.";
  } catch {
    if (!quiet) $("export-status").textContent = "Captura iniciada, mas não foi possível registrar o heartbeat. Confirme a pasta.";
  }
}
function scheduleHeartbeatWrite() {
  if (heartbeatWriteTimer !== null) return;
  heartbeatWriteTimer = setTimeout(() => {
    heartbeatWriteTimer = null;
    void writeHeartbeat(true);
  }, 250);
}
async function exporting(action) {
  $("report").disabled = $("capture").disabled = true;
  try { await action(); $("export-status").textContent = "Exportação concluída na pasta selecionada."; }
  catch { $("export-status").textContent = "Não foi possível exportar. Verifique a pasta, permissão e captura disponível."; }
  finally { $("report").disabled = $("capture").disabled = false; }
}
$("report").onclick = () => exporting(() => write(`${crypto.randomUUID()}.schema-report.json`, JSON.stringify(collector.report(), null, 2)));
$("capture").onclick = () => {
  if (!$("consent").checked) { $("export-status").textContent = "Autorize explicitamente a captura privada antes de exportar."; return; }
  $("consent").checked = false;
  return exporting(async () => {
    const capture = await collector.capture();
    await write(capture.fileName, capture.body);
    await write(capture.markerName, capture.marker);
  });
};

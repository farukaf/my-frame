import { Collector } from "./collector.mjs";
const $ = id => document.getElementById(id);
const api = globalThis.overwolf;
const collector = api ? new Collector(api, status => {
  $("status").textContent = JSON.stringify(status, null, 2);
}) : null;
$("availability").textContent = api ? "Pronto. Clique em iniciar; depois abra o Warframe." :
  "Abra este pacote como extensão local no Overwolf. O navegador comum não oferece GEP.";
if (!api) for (const button of document.querySelectorAll("button")) button.disabled = true;
$("start").onclick = () => collector.start();
$("stop").onclick = () => { collector.stop(); $("consent").checked = false; };
addEventListener("unload", () => collector?.stop());

function write(name, text) {
  const folder = $("folder").value.trim().replace(/[\\/]+$/, "");
  if (!/^[a-zA-Z]:[\\/]/.test(folder) || /[<>|"*?]/.test(folder) || folder.split(/[\\/]/).includes(".."))
    return Promise.reject(new Error("OUTPUT_FOLDER_REQUIRED"));
  return new Promise((resolve, reject) => {
    api.io.writeFileContents(`${folder}\\${name}`, text, api.io.enums.eEncoding.UTF8, false,
      result => result?.success ? resolve() : reject(new Error("WRITE_FAILED")));
  });
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

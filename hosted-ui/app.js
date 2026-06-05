const LOCAL_AGENT_URL = "http://localhost:5000";
const STATUS_URL = `${LOCAL_AGENT_URL}/api/status`;

const statusBadge = document.querySelector("#statusBadge");
const readyPanel = document.querySelector("#readyPanel");
const installPanel = document.querySelector("#installPanel");
const checkAgain = document.querySelector("#checkAgain");
const windowsDownload = document.querySelector("#windowsDownload");
const macArmDownload = document.querySelector("#macArmDownload");
const macIntelDownload = document.querySelector("#macIntelDownload");
const macTerminalInstall = document.querySelector("#macTerminalInstall");
const macArmCommand = document.querySelector("#macArmCommand");
const macIntelCommand = document.querySelector("#macIntelCommand");
const copyMacArm = document.querySelector("#copyMacArm");
const copyMacIntel = document.querySelector("#copyMacIntel");

function detectPlatform() {
  const ua = navigator.userAgent.toLowerCase();
  if (ua.includes("mac")) {
    return "mac";
  }
  if (ua.includes("windows")) {
    return "windows";
  }
  return "unknown";
}

function markRecommendedDownload() {
  const platform = detectPlatform();
  windowsDownload.classList.toggle("recommended", platform === "windows");
  macArmDownload.classList.toggle("recommended", platform === "mac");
  macIntelDownload.classList.toggle("recommended", false);
}

function getBaseUrl() {
  const url = new URL(window.location.href);
  url.hash = "";
  url.search = "";
  url.pathname = url.pathname.replace(/\/[^/]*$/, "/");
  return url.toString().replace(/\/$/, "");
}

function buildMacCommand(runtime, archiveName) {
  const baseUrl = getBaseUrl();
  const archiveUrl = `${baseUrl}/downloads/${archiveName}`;
  return `curl -fsSL "${baseUrl}/install-mac.sh" | bash -s -- "${archiveUrl}" ${runtime}`;
}

async function copyCommand(command, button) {
  await navigator.clipboard.writeText(command);
  const original = button.textContent;
  button.textContent = "Copied";
  window.setTimeout(() => {
    button.textContent = original;
  }, 1200);
}

function setChecking() {
  statusBadge.className = "status checking";
  statusBadge.textContent = "Checking local agent...";
  readyPanel.classList.add("hidden");
  installPanel.classList.add("hidden");
}

function setReady() {
  statusBadge.className = "status ready";
  statusBadge.textContent = "Agent is running";
  readyPanel.classList.remove("hidden");
  installPanel.classList.add("hidden");
  window.location.href = LOCAL_AGENT_URL;
}

function setMissing() {
  statusBadge.className = "status missing";
  statusBadge.textContent = "Agent not found";
  readyPanel.classList.add("hidden");
  installPanel.classList.remove("hidden");
}

async function checkAgent() {
  setChecking();

  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), 1800);

  try {
    const response = await fetch(STATUS_URL, {
      signal: controller.signal,
      mode: "cors"
    });

    window.clearTimeout(timeout);

    if (!response.ok) {
      setMissing();
      return;
    }

    setReady();
  } catch {
    window.clearTimeout(timeout);
    setMissing();
  }
}

checkAgain.addEventListener("click", checkAgent);
copyMacArm.addEventListener("click", () => copyCommand(macArmCommand.textContent, copyMacArm));
copyMacIntel.addEventListener("click", () => copyCommand(macIntelCommand.textContent, copyMacIntel));

macArmCommand.textContent = buildMacCommand("osx-arm64", "corporate-rag-osx-arm64.zip");
macIntelCommand.textContent = buildMacCommand("osx-x64", "corporate-rag-osx-x64.zip");
markRecommendedDownload();
checkAgent();

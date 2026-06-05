const LOCAL_AGENT_URL = "http://localhost:5000";
const STATUS_URL = `${LOCAL_AGENT_URL}/api/status`;

const statusBadge = document.querySelector("#statusBadge");
const readyPanel = document.querySelector("#readyPanel");
const installPanel = document.querySelector("#installPanel");
const checkAgain = document.querySelector("#checkAgain");
const windowsDownload = document.querySelector("#windowsDownload");
const macArmDownload = document.querySelector("#macArmDownload");
const macIntelDownload = document.querySelector("#macIntelDownload");

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

markRecommendedDownload();
checkAgent();

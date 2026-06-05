const statusText = document.querySelector("#status");
const chunkCount = document.querySelector("#chunkCount");
const cachedChunkCount = document.querySelector("#cachedChunkCount");
const lastFile = document.querySelector("#lastFile");
const indexState = document.querySelector("#indexState");
const progressPercent = document.querySelector("#progressPercent");
const progressFill = document.querySelector("#progressFill");
const progressDetail = document.querySelector("#progressDetail");
const progressTime = document.querySelector("#progressTime");
const answerBadge = document.querySelector("#answerBadge");
const answerActivity = document.querySelector("#answerActivity");
const answerState = document.querySelector("#answerState");
const uploadForm = document.querySelector("#uploadForm");
const pdfInput = document.querySelector("#pdfInput");
const indexButton = document.querySelector("#indexButton");
const askForm = document.querySelector("#askForm");
const clearCacheButton = document.querySelector("#clearCacheButton");
const questionInput = document.querySelector("#questionInput");
const askButton = document.querySelector("#askButton");
const messages = document.querySelector("#messages");
const modelSettingsButton = document.querySelector("#modelSettingsButton");
const dependencyModal = document.querySelector("#dependencyModal");
const dependencySummary = document.querySelector("#dependencySummary");
const dependencyIssues = document.querySelector("#dependencyIssues");
const dependencyCommands = document.querySelector("#dependencyCommands");
const dependencyClose = document.querySelector("#dependencyClose");
const dependencyRefresh = document.querySelector("#dependencyRefresh");
const setupLoader = document.querySelector("#setupLoader");
const setupLoaderTitle = document.querySelector("#setupLoaderTitle");
const setupLoaderText = document.querySelector("#setupLoaderText");
const stepOllama = document.querySelector("#stepOllama");
const stepModel = document.querySelector("#stepModel");
const stepReady = document.querySelector("#stepReady");
const stepOllamaText = document.querySelector("#stepOllamaText");
const stepModelText = document.querySelector("#stepModelText");
const stepReadyText = document.querySelector("#stepReadyText");
const installedModelSelect = document.querySelector("#installedModelSelect");
const installOllamaButton = document.querySelector("#installOllamaButton");
const useInstalledModel = document.querySelector("#useInstalledModel");
const useFastModel = document.querySelector("#useFastModel");
const useQualityModel = document.querySelector("#useQualityModel");
let progressTimer = null;
let answerTimer = null;
let answerStartedAt = null;
let modelPresets = [];
let installedModels = [];
let ollamaReachable = false;
let setupBusy = false;

async function readJson(response) {
  const text = await response.text();
  if (!text) {
    return {};
  }

  try {
    return JSON.parse(text);
  } catch {
    return { error: text };
  }
}

function getError(data, fallback) {
  return data.error || data.detail || data.title || fallback;
}

async function loadStatus() {
  const response = await fetch("/api/status");
  const data = await readJson(response);
  statusText.textContent = `Provider: ${data.provider} | Chunks: ${data.chunks}`;
  if (data.modelSetup?.isRunning) {
    statusText.textContent = `Provider: ${data.provider} | Model setup: ${data.modelSetup.message}`;
  }
  chunkCount.textContent = data.chunks;
  cachedChunkCount.textContent = data.cachedChunks ?? 0;
  updateProgress(data.indexing);
  return data;
}

function updateProgress(indexing) {
  if (!indexing) {
    return;
  }

  const percent = indexing.percent ?? 0;
  const current = indexing.current ?? 0;
  const total = indexing.total ?? 0;

  progressPercent.textContent = `${percent}%`;
  progressFill.style.width = `${percent}%`;
  progressDetail.textContent = `${current} of ${total} chunks`;

  const elapsed = formatDuration(indexing.elapsedSeconds ?? 0);
  const remaining = indexing.estimatedRemainingSeconds;
  progressTime.textContent = remaining === null || remaining === undefined
    ? `Elapsed: ${elapsed}`
    : `Elapsed: ${elapsed} | ETA: ${formatDuration(remaining)}`;

  if (indexing.fileName) {
    lastFile.textContent = indexing.fileName;
  }

  setState(indexing.message || "Waiting for PDF", Boolean(indexing.error));
}

function formatDuration(totalSeconds) {
  const seconds = Math.max(0, Math.round(totalSeconds));
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;

  if (minutes <= 0) {
    return `${rest}s`;
  }

  return `${minutes}m ${rest}s`;
}

function startProgressPolling() {
  stopProgressPolling();
  progressTimer = window.setInterval(async () => {
    try {
      await loadStatus();
    } catch {
      stopProgressPolling();
    }
  }, 500);
}

function stopProgressPolling() {
  if (progressTimer) {
    window.clearInterval(progressTimer);
    progressTimer = null;
  }
}

function setState(text, error = false) {
  indexState.textContent = text;
  indexState.classList.toggle("error", error);
}

function addMessage(role, text) {
  const article = document.createElement("article");
  article.className = `message ${role}`;

  const label = document.createElement("div");
  label.className = "role";
  label.textContent = role === "user" ? "You" : "Assistant";

  const paragraph = document.createElement("p");
  paragraph.textContent = text;

  article.append(label, paragraph);
  messages.append(article);
  messages.scrollTop = messages.scrollHeight;
}

function addThinkingMessage() {
  const article = document.createElement("article");
  article.className = "message assistant pending";

  const label = document.createElement("div");
  label.className = "role";
  label.textContent = "Assistant";

  const typing = document.createElement("div");
  typing.className = "typing";
  typing.setAttribute("aria-label", "Generating answer");
  typing.append(document.createElement("span"), document.createElement("span"), document.createElement("span"));

  article.append(label, typing);
  messages.append(article);
  messages.scrollTop = messages.scrollHeight;

  return article;
}

function setAnswerBusy(isBusy, message) {
  answerBadge.textContent = isBusy ? "Generating" : "Idle";
  answerActivity.classList.toggle("active", isBusy);
  answerState.textContent = message;
  askButton.textContent = isBusy ? "Thinking..." : "Ask";

  if (isBusy) {
    answerStartedAt = Date.now();
    if (answerTimer) {
      window.clearInterval(answerTimer);
    }

    answerTimer = window.setInterval(() => {
      const elapsed = Math.round((Date.now() - answerStartedAt) / 1000);
      answerState.textContent = `${message} | Elapsed: ${formatDuration(elapsed)}`;
    }, 500);
  } else if (answerTimer) {
    window.clearInterval(answerTimer);
    answerTimer = null;
  }
}

async function checkDependencies(showWhenOk = false) {
  const response = await fetch("/api/dependencies");
  const data = await readJson(response);
  await loadModels().catch(() => {});

  dependencySummary.textContent = data.ok
    ? "Choose which local model this app should use."
    : "Install or download the missing items before indexing and asking questions.";

  if (data.modelSetup?.isRunning) {
    dependencySummary.textContent = data.modelSetup.currentModel
      ? `Downloading ${data.modelSetup.currentModel}. Keep this window open.`
      : data.modelSetup.message;
  }

  updateSetupWizard(data);

  dependencyIssues.innerHTML = "";
  const issues = data.issues || [];
  if (data.modelSetup?.isRunning) {
    const item = document.createElement("div");
    item.className = "dependency-item active";
    const title = document.createElement("strong");
    title.textContent = "Model setup is running";
    const detail = document.createElement("span");
    detail.textContent = data.modelSetup.message;
    item.append(title, detail);
    dependencyIssues.append(item);
  }

  if (issues.length === 0) {
    const item = document.createElement("div");
    item.className = "dependency-item";
    item.innerHTML = "<strong>Ready</strong><span>No missing dependencies found.</span>";
    dependencyIssues.append(item);
  } else {
    for (const issue of issues) {
      const item = document.createElement("div");
      item.className = "dependency-item";
      const title = document.createElement("strong");
      title.textContent = issue.title;
      const detail = document.createElement("span");
      detail.textContent = issue.detail;
      item.append(title, detail);
      dependencyIssues.append(item);
    }
  }

  dependencyCommands.textContent = (data.commands || []).join("\n") || "No commands required.";
  dependencyModal.classList.remove("hidden");

  if (data.modelSetup?.isRunning) {
    window.setTimeout(() => {
      checkDependencies(true).catch(() => {});
    }, 1500);
  }
}

function updateSetupWizard(data) {
  setupBusy = Boolean(data.modelSetup?.isRunning);
  const setupMessage = data.modelSetup?.message || "Please wait.";
  const hasChatModels = installedModels.some((model) => !model.toLowerCase().includes("embed"));

  setupLoader.classList.toggle("hidden", !setupBusy);
  setupLoaderTitle.textContent = setupBusy ? "Setup is running" : "Ready";
  setupLoaderText.textContent = setupMessage;

  dependencyClose.disabled = setupBusy;
  dependencyRefresh.disabled = setupBusy;

  stepOllama.classList.toggle("active", !ollamaReachable || setupBusy);
  stepOllama.classList.toggle("done", ollamaReachable);
  stepOllama.classList.toggle("locked", setupBusy && !setupMessage.toLowerCase().includes("ollama"));
  stepOllamaText.textContent = ollamaReachable
    ? "Ollama is installed and reachable."
    : "Ollama is required before downloading models.";

  stepModel.classList.toggle("active", ollamaReachable && !setupBusy);
  stepModel.classList.toggle("done", ollamaReachable && hasChatModels);
  stepModel.classList.toggle("locked", !ollamaReachable || setupBusy);
  stepModelText.textContent = hasChatModels
    ? "Use an installed model or download another preset."
    : "Choose Fast for most PCs, or Quality for stronger answers.";

  stepReady.classList.toggle("active", ollamaReachable && hasChatModels && !setupBusy);
  stepReady.classList.toggle("done", ollamaReachable && hasChatModels && !setupBusy);
  stepReady.classList.toggle("locked", !ollamaReachable || setupBusy || !hasChatModels);
  stepReadyText.textContent = ollamaReachable && hasChatModels
    ? "Setup is ready. Close this panel, upload a PDF, then index it."
    : "This step unlocks after Ollama and a chat model are ready.";

  installOllamaButton.disabled = setupBusy || ollamaReachable;
  useInstalledModel.disabled = setupBusy || !ollamaReachable || !installedModelSelect.value;
  useFastModel.disabled = setupBusy || !ollamaReachable;
  useQualityModel.disabled = setupBusy || !ollamaReachable;
  installedModelSelect.disabled = setupBusy || !ollamaReachable;
}

function setSetupBusy(message) {
  setupBusy = true;
  setupLoader.classList.remove("hidden");
  setupLoaderTitle.textContent = "Setup is running";
  setupLoaderText.textContent = message;
  dependencySummary.textContent = message;
  dependencyClose.disabled = true;
  dependencyRefresh.disabled = true;
  installOllamaButton.disabled = true;
  useInstalledModel.disabled = true;
  useFastModel.disabled = true;
  useQualityModel.disabled = true;
  installedModelSelect.disabled = true;
}

async function loadModels() {
  const response = await fetch("/api/models");
  const data = await readJson(response);
  modelPresets = data.presets || [];
  installedModels = data.installedModels || [];
  ollamaReachable = Boolean(data.ollamaReachable);

  installedModelSelect.innerHTML = "";
  const chatModels = (data.installedModels || []).filter((model) => !model.includes("embed"));
  if (chatModels.length === 0) {
    const option = document.createElement("option");
    option.value = "";
    option.textContent = data.ollamaReachable ? "No chat models installed" : "Ollama is not reachable";
    installedModelSelect.append(option);
  } else {
    for (const model of chatModels) {
      const option = document.createElement("option");
      option.value = model;
      option.textContent = model === data.selected?.chatModel ? `${model} (current)` : model;
      installedModelSelect.append(option);
    }
  }

  useInstalledModel.disabled = !installedModelSelect.value;
  installOllamaButton.classList.toggle("hidden", ollamaReachable);
  useFastModel.disabled = !ollamaReachable;
  useQualityModel.disabled = !ollamaReachable;
  updatePresetButtons();
  return data;
}

function hasModel(model) {
  if (!model) {
    return false;
  }

  return installedModels.some((installed) =>
    installed.toLowerCase() === model.toLowerCase()
    || installed.toLowerCase() === `${model}:latest`.toLowerCase()
    || `${installed}:latest`.toLowerCase() === model.toLowerCase());
}

function updatePresetButtons() {
  const fast = modelPresets.find((item) => item.name === "Fast");
  const quality = modelPresets.find((item) => item.name === "Quality");

  if (fast) {
    useFastModel.textContent = hasModel(fast.chatModel) ? "Use fast" : "Download fast";
  }

  if (quality) {
    useQualityModel.textContent = hasModel(quality.chatModel) ? "Use quality" : "Download quality";
  }
}

async function selectModel(chatModel, embeddingModel = "nomic-embed-text:latest") {
  if (setupBusy) {
    return;
  }

  if (!chatModel) {
    dependencySummary.textContent = "Select an installed chat model first.";
    dependencyModal.classList.remove("hidden");
    return;
  }

  dependencySummary.textContent = `Preparing ${chatModel}. The app will restart after setup.`;
  setSetupBusy(`Preparing ${chatModel}. The app will restart after setup.`);
  dependencyModal.classList.remove("hidden");

  const response = await fetch("/api/models/select", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ chatModel, embeddingModel })
  });

  const data = await readJson(response);
  if (!response.ok) {
    dependencySummary.textContent = getError(data, "Model selection failed");
    setupBusy = false;
    updateSetupWizard({ modelSetup: { isRunning: false, message: dependencySummary.textContent } });
    return;
  }

  window.setTimeout(() => {
    checkDependencies(true).catch(() => {});
  }, 1000);
}

dependencyClose.addEventListener("click", () => {
  if (setupBusy) {
    return;
  }

  dependencyModal.classList.add("hidden");
});

dependencyRefresh.addEventListener("click", () => {
  checkDependencies(true).catch((error) => {
    dependencySummary.textContent = error.message;
    dependencyModal.classList.remove("hidden");
  });
});

modelSettingsButton.addEventListener("click", () => {
  checkDependencies(true).catch((error) => {
    dependencySummary.textContent = error.message;
    dependencyModal.classList.remove("hidden");
  });
});

useInstalledModel.addEventListener("click", () => {
  selectModel(installedModelSelect.value);
});

installOllamaButton.addEventListener("click", async () => {
  if (setupBusy) {
    return;
  }

  installOllamaButton.disabled = true;
  setSetupBusy("Installing Ollama. Approve Windows prompts if shown.");
  dependencyModal.classList.remove("hidden");

  try {
    const response = await fetch("/api/ollama/install", {
      method: "POST"
    });
    const data = await readJson(response);
    if (!response.ok) {
      throw new Error(getError(data, "Ollama installation failed"));
    }

    window.setTimeout(() => {
      checkDependencies(true).catch(() => {});
    }, 1000);
  } catch (error) {
    dependencySummary.textContent = error.message;
    setupBusy = false;
    updateSetupWizard({ modelSetup: { isRunning: false, message: error.message } });
  }
});

useFastModel.addEventListener("click", () => {
  const preset = modelPresets.find((item) => item.name === "Fast");
  selectModel(preset?.chatModel || "llama3.2:3b", preset?.embeddingModel || "nomic-embed-text:latest");
});

useQualityModel.addEventListener("click", () => {
  const preset = modelPresets.find((item) => item.name === "Quality");
  selectModel(preset?.chatModel || "llama3.1:8b", preset?.embeddingModel || "nomic-embed-text:latest");
});

uploadForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const file = pdfInput.files[0];
  if (!file) {
    setState("Select a PDF first", true);
    return;
  }

  const formData = new FormData();
  formData.append("file", file);

  indexButton.disabled = true;
  setState("Indexing PDF...");
  progressPercent.textContent = "0%";
  progressFill.style.width = "0%";
  progressDetail.textContent = "Preparing chunks";
  progressTime.textContent = "Elapsed: 0s";
  startProgressPolling();

  try {
    const response = await fetch("/api/index", {
      method: "POST",
      body: formData
    });

    const data = await readJson(response);
    if (!response.ok) {
      throw new Error(getError(data, "Indexing failed"));
    }

    lastFile.textContent = data.file;
    chunkCount.textContent = data.chunks;
    cachedChunkCount.textContent = data.cachedChunks ?? data.chunks;
    await loadStatus();
    setState("PDF indexed");
    addMessage("assistant", `Indexed ${data.file}. Chunks in memory: ${data.chunks}.`);
  } catch (error) {
    setState(error.message, true);
    addMessage("assistant", error.message);
  } finally {
    stopProgressPolling();
    await loadStatus().catch(() => {});
    indexButton.disabled = false;
  }
});

clearCacheButton.addEventListener("click", async () => {
  clearCacheButton.disabled = true;
  setState("Clearing cache...");

  try {
    const response = await fetch("/api/cache/clear", {
      method: "POST"
    });
    const data = await readJson(response);
    if (!response.ok) {
      throw new Error(getError(data, "Cache clear failed"));
    }

    chunkCount.textContent = data.chunks;
    cachedChunkCount.textContent = data.cachedChunks;
    progressPercent.textContent = "0%";
    progressFill.style.width = "0%";
    progressDetail.textContent = "0 of 0 chunks";
    progressTime.textContent = "Elapsed: 0s";
    lastFile.textContent = "None";
    setState("Cache cleared");
    addMessage("assistant", "Cache cleared. Upload and index a PDF to rebuild the knowledge base.");
  } catch (error) {
    setState(error.message, true);
    addMessage("assistant", error.message);
  } finally {
    clearCacheButton.disabled = false;
    await loadStatus().catch(() => {});
  }
});

askForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const question = questionInput.value.trim();
  if (!question) {
    return;
  }

  addMessage("user", question);
  questionInput.value = "";
  askButton.disabled = true;
  setAnswerBusy(true, "Retrieving chunks and generating answer");
  const thinkingMessage = addThinkingMessage();
  const requestStartedAt = Date.now();

  try {
    const response = await fetch("/api/ask", {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({ question })
    });

    const data = await readJson(response);
    if (!response.ok) {
      throw new Error(getError(data, "Question failed"));
    }

    thinkingMessage.remove();
    addMessage("assistant", data.answer);
    const elapsed = Math.round((Date.now() - requestStartedAt) / 1000);
    setAnswerBusy(false, `Answer complete | Took: ${formatDuration(elapsed)}`);
    await loadStatus();
  } catch (error) {
    thinkingMessage.remove();
    addMessage("assistant", error.message);
    setAnswerBusy(false, "Answer failed");
  } finally {
    askButton.disabled = false;
    questionInput.focus();
  }
});

questionInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter" && !event.shiftKey) {
    event.preventDefault();
    askForm.requestSubmit();
  }
});

loadStatus().catch(() => {
  statusText.textContent = "Provider: unknown | Chunks: 0";
});

checkDependencies(true).catch(() => {});

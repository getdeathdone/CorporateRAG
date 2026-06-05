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
const dependencyModal = document.querySelector("#dependencyModal");
const dependencySummary = document.querySelector("#dependencySummary");
const dependencyIssues = document.querySelector("#dependencyIssues");
const dependencyCommands = document.querySelector("#dependencyCommands");
const dependencyClose = document.querySelector("#dependencyClose");
const dependencyRefresh = document.querySelector("#dependencyRefresh");
let progressTimer = null;
let answerTimer = null;
let answerStartedAt = null;

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

  if (data.ok && !showWhenOk) {
    dependencyModal.classList.add("hidden");
    return;
  }

  dependencySummary.textContent = data.ok
    ? "Everything required for the selected provider is available."
    : "Install or download the missing items before indexing and asking questions.";

  dependencyIssues.innerHTML = "";
  const issues = data.issues || [];
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
}

dependencyClose.addEventListener("click", () => {
  dependencyModal.classList.add("hidden");
});

dependencyRefresh.addEventListener("click", () => {
  checkDependencies(true).catch((error) => {
    dependencySummary.textContent = error.message;
    dependencyModal.classList.remove("hidden");
  });
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

checkDependencies().catch(() => {});

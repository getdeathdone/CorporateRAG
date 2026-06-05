const statusText = document.querySelector("#status");
const chunkCount = document.querySelector("#chunkCount");
const cachedChunkCount = document.querySelector("#cachedChunkCount");
const lastFile = document.querySelector("#lastFile");
const indexState = document.querySelector("#indexState");
const progressPercent = document.querySelector("#progressPercent");
const progressFill = document.querySelector("#progressFill");
const progressDetail = document.querySelector("#progressDetail");
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
let progressTimer = null;

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

  if (indexing.fileName) {
    lastFile.textContent = indexing.fileName;
  }

  setState(indexing.message || "Waiting for PDF", Boolean(indexing.error));
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
}

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
    setAnswerBusy(false, "Answer complete");
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

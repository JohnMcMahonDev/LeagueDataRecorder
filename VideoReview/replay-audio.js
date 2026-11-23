"use strict";
(() => {
  const audio = document.getElementById("audio");
  let clock = null, received = 0, pending = false, revision = 0, lastAttempt = -Infinity, lastSeek = -Infinity;
  const post = data => window.chrome?.webview?.postMessage(data);
  function stop() { revision++; audio.pause(); }
  function sync() {
    const now = performance.now();
    if (!clock || now - received > 700 || !Number.isFinite(clock.time) || !Number.isFinite(clock.speed)) { stop(); return; }
    const speed = clock.speed;
    const target = clock.time + (clock.paused ? 0 : (now - received) / 1000 * speed);
    if (!Number.isFinite(audio.duration)) return;
    if (target < 0 || target >= audio.duration || speed <= 0 || speed < .25 || speed > 4) { stop(); return; }
    if (audio.playbackRate !== speed) { stop(); audio.playbackRate = speed; }
    const drift = target - audio.currentTime;
    // Immediate discontinuities, including backward seeks while paused, replace
    // pending audio. Small drift is corrected without repeatedly seeking a WAV.
    if (Math.abs(drift) > .20 && (now - lastSeek > 250 || Math.abs(drift) > 1)) {
      stop(); audio.currentTime = target; lastSeek = now;
    }
    if (clock.paused) { stop(); return; }
    if (!audio.seeking && audio.readyState >= 2 && audio.paused && !pending && now - lastAttempt > 500) {
      pending = true; lastAttempt = now;
      const request = revision;
      audio.play().then(() => { if (revision !== request) audio.pause(); })
        .catch(error => { if (error.name !== "AbortError") post({ type: "error", message: "Playback unavailable: " + error.message }); })
        .finally(() => { pending = false; });
    }
  }
  window.chrome?.webview?.addEventListener("message", e => {
    const data = e.data;
    if (data.type === "load") {
      stop(); audio.src = data.audio; audio.volume = data.volume; audio.muted = data.muted === true;
      audio.preservesPitch = true; audio.load();
    } else if (data.type === "clock") {
      clock = data; received = performance.now(); sync();
    }
  });
  audio.addEventListener("loadedmetadata", sync); audio.addEventListener("seeked", sync);
  audio.addEventListener("error", () => { stop(); post({ type: "error", message: "Could not decode the saved comms WAV." }); });
  setInterval(sync, 50);
  post({ type: "ready" });
})();

"use strict";
(() => {
  const $ = id => document.getElementById(id);
  const video = $("video"), audio = $("audio");
  const state = { media: null, cues: [], prefixEnds: [], cardKey: "", captions: true,
    fights: [], visibleFights: [], fightLayoutKey: "", activeFight: null, fightHover: null, fightHoverTimer: null,
    objectiveCalls: [], visibleObjectives: [], objectiveLayoutKey: "", objectiveRevision: 0, activeObjectiveKey: "", selectedObjective: "",
    objectiveEvents: [], visibleTaken: [], takenLayoutKey: "", takenRevision: 0, takenStatus: "", activeTaken: "",
    timelineSelection: ["fights", "calls", "objectives", "gold", "outlook"], gold: [], goldStatus: "", goldLayoutKey: "", goldMaxGap: 15, goldInterpolate: false, toneVisible: [],
    tone: [], toneStatus: "", toneWindowSeconds: 20, toneAudioShift: 0, toneRevision: 0, toneLayoutKey: "", activeTone: null,
    offset: 0, audioCorrection: 0, captionAudioShift: 0, audioBuffering: false, muted: false, volume: .8, rate: 1, playing: false, buffering: false,
    loading: false, mediaFailed: false, movingHost: false, fullscreen: false, pendingSeek: null, frame: null, captionStatus: "",
    playRevision: 0, transportRevision: 0, playPending: new Map(), alignAudio: false,
    audioBlocked: false, lastAudioSeek: -Infinity };
  const post = data => window.chrome?.webview?.postMessage(data);
  const master = () => state.media?.video ? video : audio;
  const hasMedia = () => Boolean(state.media?.video || state.media?.audio);
  const duration = () => Number.isFinite(master().duration) ? master().duration : 0;
  const clamp = (n, lo, hi) => Math.min(hi, Math.max(lo, n));
  const finite = n => typeof n === "number" && Number.isFinite(n);
  function time(n) {
    n = Math.max(0, Math.floor(n || 0));
    const mm = Math.floor(n / 60), ss = String(n % 60).padStart(2, "0");
    return mm >= 60 ? `${Math.floor(mm / 60)}:${String(mm % 60).padStart(2, "0")}:${ss}` : `${String(mm).padStart(2, "0")}:${ss}`;
  }
  function status(text, error = false) {
    $("status").textContent = text;
    $("status").title = text;
    $("status").classList.toggle("error", error);
  }
  function regularStatus() {
    if (!hasMedia()) return;
    const note = state.captionStatus || state.media.timingNote ||
      (state.media.audio ? "Space to play · ← / → to seek" : "No separate voice recording · Captions follow the saved transcript");
    status(note);
  }
  function controls() {
    const active = hasMedia();
    for (const id of ["play", "back", "forward", "seek"]) $(id).disabled = !active;
    // Native paused/waiting state changes during seeks and buffering. The
    // transport button reflects the user request, so it remains stable.
    const paused = !active || !state.playing;
    $("play").textContent = paused ? "Play" : "Pause";
    $("play").setAttribute("aria-label", paused ? "Play" : "Pause");
    $("elapsed").textContent = time(active ? master().currentTime : 0);
    $("duration").textContent = time(duration());
    $("seek").value = duration() ? String(master().currentTime / duration() * 1000) : "0";
    $("seek").setAttribute("aria-valuetext", `${$("elapsed").textContent} of ${$("duration").textContent}`);
    highlightFight();
    renderObjectiveTimeline();
    highlightObjectiveCall();
    renderTakenTimeline(); highlightTaken();
    renderToneTimeline(); highlightTone();
    renderGoldTimeline(); highlightGold();
    renderLoading();
  }
  function closeFightList(returnFocus = false) {
    const open = !$("fightList").hidden;
    if (open) hideFightTooltip();
    $("fightList").hidden = true;
    $("fightListToggle").setAttribute("aria-expanded", "false");
    if (open && returnFocus) $("fightListToggle").focus();
  }
  function prepareFights(fights) {
    state.fights = (Array.isArray(fights) ? fights : [])
      .filter(f => f && finite(f.start) && finite(f.end) && f.end > f.start && f.end > 0 &&
        finite(f.gameStart) && finite(f.gameEnd) && f.gameStart >= 0 && f.gameEnd > f.gameStart)
      .sort((a, b) => a.start - b.start)
      .map((f, i) => {
        const kills = Number.isInteger(f.kills) && f.kills >= 0 ? f.kills : 0;
        const scored = Number.isInteger(f.allyKills) && f.allyKills >= 0 && Number.isInteger(f.enemyKills) &&
          f.enemyKills >= 0 && f.allyKills + f.enemyKills === kills;
        const expected = f.allyKills > f.enemyKills ? "win" : f.allyKills < f.enemyKills ? "loss" : "even";
        const outcome = scored && f.outcome === expected ? expected : "unknown";
        return { ...f, number: Number.isInteger(f.id) && f.id > 0 ? f.id : i + 1, kills, outcome,
          allyKills: outcome === "unknown" ? null : f.allyKills, enemyKills: outcome === "unknown" ? null : f.enemyKills,
          participants: typeof f.participants === "string" ? f.participants : "",
          outcomeNote: typeof f.outcomeNote === "string" ? f.outcomeNote : "Ally team or kill attribution is unavailable.",
          champions: (Array.isArray(f.champions) ? f.champions : []).filter(c => c && typeof c.champion === "string") };
      });
    hideFightTooltip();
    closeFightList();
    state.fightLayoutKey = "";
    renderFightTimeline();
  }
  function fightLabel(fight) {
    return `Fight · ${time(fight.gameStart)}–${time(fight.gameEnd)} · ${fight.kills} ${fight.kills === 1 ? "kill" : "kills"}`;
  }
  function fightResult(fight) {
    const label = { win: "Ally win", loss: "Ally loss", even: "Even trade", unknown: "Outcome unknown" }[fight.outcome];
    return label + (fight.allyKills !== null ? ` · ${fight.allyKills}–${fight.enemyKills}` : "");
  }
  function hideFightTooltip() {
    if (state.fightHoverTimer !== null) clearTimeout(state.fightHoverTimer);
    state.fightHoverTimer = null;
    state.fightHover?.anchor.removeAttribute("aria-describedby");
    state.fightHover = null;
    $("fightTooltip").hidden = true;
    $("fightTooltip").replaceChildren();
  }
  function scheduleFightTooltipHide() {
    if (state.fightHoverTimer !== null) clearTimeout(state.fightHoverTimer);
    state.fightHoverTimer = setTimeout(() => {
      state.fightHoverTimer = null;
      const anchor = state.fightHover?.anchor;
      if (anchor && document.activeElement === anchor && anchor.matches(":focus-visible")) return;
      hideFightTooltip();
    }, 150);
  }
  function showFightTooltip(fight, anchor) {
    hideFightTooltip();
    const card = $("fightTooltip");
    state.fightHover = { fight, anchor };
    anchor.setAttribute("aria-describedby", "fightTooltip");
    card.dataset.outcome = fight.outcome;
    const title = document.createElement("div"); title.className = "fight-card-title";
    const name = document.createElement("strong"); name.textContent = "Fight";
    const result = document.createElement("span"); result.className = "fight-result"; result.textContent = fightResult(fight);
    title.append(name, result);
    const timing = document.createElement("div"); timing.className = "fight-card-time";
    timing.textContent = `${time(fight.gameStart)}–${time(fight.gameEnd)} game time · video ${time(fight.start)}`;
    card.append(title, timing);
    const groups = [["ally", "Allies"], ["enemy", "Enemies"], ["blue", "Blue team"], ["red", "Red team"], ["unknown", "Team unknown"]];
    for (const [side, label] of groups) {
      const people = fight.champions.filter(c => (groups.some(g => g[0] === c.side) ? c.side : "unknown") === side);
      if (!people.length) continue;
      const team = document.createElement("section"); team.className = "fight-team"; team.dataset.side = side;
      const heading = document.createElement("div"); heading.className = "fight-team-label";
      heading.textContent = label;
      const portraits = document.createElement("div"); portraits.className = "fight-champions";
      for (const person of people) {
        const champion = person.champion || "Unknown champion";
        const item = document.createElement("div"); item.className = "fight-champion";
        const killsKnown = Number.isInteger(person.kills) && person.kills >= 0;
        const deathsKnown = Number.isInteger(person.deaths) && person.deaths >= 0;
        const killText = killsKnown ? `${person.kills} ${person.kills === 1 ? "kill" : "kills"}` : "Kills unknown";
        const deathText = deathsKnown ? `${person.deaths} ${person.deaths === 1 ? "death" : "deaths"}` : "Deaths unknown";
        const stats = killsKnown || deathsKnown ? `${killText} · ${deathText}` : "Kill/death details unavailable";
        item.title = champion + (typeof person.name === "string" && person.name ? ` · ${person.name}` : "") + `\n${stats}`;
        item.setAttribute("role", "img");
        item.setAttribute("aria-label", item.title);
        const fallback = () => {
          const image = document.createElement("span"); image.className = "fight-icon fallback";
          image.textContent = champion === "Unknown champion" ? "?" : champion.slice(0, 2).toUpperCase();
          image.setAttribute("aria-hidden", "true"); return image;
        };
        let icon;
        if (typeof person.icon === "string" && /^data:image\/(?:png|jpeg|webp);base64,/.test(person.icon)) {
          icon = document.createElement("img"); icon.className = "fight-icon"; icon.alt = champion; icon.src = person.icon;
          icon.addEventListener("error", () => icon.replaceWith(fallback()), { once: true });
        } else icon = fallback();
        const portrait = document.createElement("span"); portrait.className = "fight-portrait";
        portrait.append(icon);
        if (deathsKnown && person.deaths > 0) {
          portrait.classList.toggle("died", true);
          const death = document.createElement("span"); death.className = "fight-death-mark";
          death.textContent = "×"; death.setAttribute("aria-hidden", "true"); portrait.append(death);
        }
        if (killsKnown && person.kills > 0) {
          const tally = document.createElement("span"); tally.className = "fight-kill-tallies";
          tally.setAttribute("aria-hidden", "true");
          for (let i = 0; i < Math.min(5, person.kills); i++) {
            const tick = document.createElement("span"); tick.className = "fight-kill-tick"; tally.append(tick);
          }
          if (person.kills > 5) {
            const extra = document.createElement("span"); extra.className = "fight-kill-extra"; extra.textContent = "+"; tally.append(extra);
          }
          portrait.append(tally);
        }
        const caption = document.createElement("span"); caption.className = "fight-champion-name"; caption.textContent = champion;
        item.append(portrait, caption); portraits.append(item);
      }
      team.append(heading, portraits); card.append(team);
    }
    if (!fight.champions.length) {
      const missing = document.createElement("div"); missing.className = "fight-card-note";
      missing.textContent = fight.participants || "Champion identities are unavailable."; card.append(missing);
    }
    if (fight.champions.length) {
      const legend = document.createElement("div"); legend.className = "fight-card-legend";
      legend.textContent = "× died · | kill · + more · hover for counts";
      card.append(legend);
    }
    const note = document.createElement("div"); note.className = "fight-card-note"; note.textContent = fight.outcomeNote;
    card.append(note);
    const target = anchor.getBoundingClientRect();
    const roomAbove = Math.max(0, target.top - 16), roomBelow = Math.max(0, window.innerHeight - target.bottom - 16);
    const above = roomAbove >= roomBelow;
    // A short player must keep the marker clickable. Limit the card to the
    // available side of its anchor and let the champion card scroll if needed.
    card.style.maxHeight = `${Math.max(1, above ? roomAbove : roomBelow)}px`;
    card.hidden = false;
    const bounds = card.getBoundingClientRect();
    const left = clamp(target.left + target.width / 2 - bounds.width / 2, 8, Math.max(8, window.innerWidth - bounds.width - 8));
    const top = above ? Math.max(8, target.top - bounds.height - 8) : target.bottom + 8;
    card.style.left = `${left}px`; card.style.top = `${top}px`;
  }
  function attachFightTooltip(element, fight) {
    element.addEventListener("pointerenter", () => showFightTooltip(fight, element));
    element.addEventListener("pointerleave", scheduleFightTooltipHide);
    element.addEventListener("focus", () => showFightTooltip(fight, element));
    element.addEventListener("blur", scheduleFightTooltipHide);
  }
  function jumpToFight(fight) {
    // This is a video position, unlike transcript seeks. Voice and caption
    // corrections must never move a game event on the timeline.
    hideFightTooltip();
    seekMedia(Math.max(0, fight.start));
    closeFightList(true);
  }
  function renderFightTimeline() {
    const shown = timelineVisible("fights") && Boolean(state.media?.video);
    $("fightTimeline").hidden = !shown;
    if (!shown) {
      hideFightTooltip();
      state.visibleFights = []; state.activeFight = null; state.fightLayoutKey = "";
      $("fightMarkers").replaceChildren(); $("fightList").replaceChildren();
      $("fightCount").textContent = "";
      closeFightList();
      return;
    }
    const length = duration(), width = $("fightMarkers").clientWidth;
    const key = `${length}:${width}`;
    if (key === state.fightLayoutKey) return;
    hideFightTooltip();
    state.fightLayoutKey = key;
    state.visibleFights = length > 0 ? state.fights.filter(f => f.start < length && f.end > 0) : [];
    const note = state.media.fightStatus || "Estimated fight windows from saved kills and assists.";
    const count = state.visibleFights.length;
    $("fightTimeline").title = note;
    $("fightCount").textContent = count ? String(count) : "—";
    $("fightCount").title = `${count} of ${state.fights.length} estimated fights in this video. ${note}`;
    $("fightListToggle").disabled = count === 0;
    $("fightMarkers").classList.toggle("fight-empty", count === 0);
    const markers = [], entries = [];
    for (const fight of state.visibleFights) {
      const label = fightLabel(fight);
      const detail = `${label} (game time) · ${fightResult(fight)}\n${fight.participants}\nJump to video ${time(fight.start)}. Includes the fight lead-in.`;
      const marker = document.createElement("button");
      marker.type = "button"; marker.className = "fight-marker";
      marker.textContent = fight.allyKills !== null ? `${fight.allyKills}–${fight.enemyKills}` : "?"; marker.title = "";
      marker.dataset.scored = String(fight.allyKills !== null && fight.kills > 0);
      if (fight.allyKills !== null && fight.kills > 0)
        marker.style.setProperty("--ally-share", `${fight.allyKills / fight.kills * 100}%`);
      marker.setAttribute("aria-label", detail);
      marker.dataset.fight = String(fight.number);
      marker.dataset.outcome = fight.outcome;
      attachFightTooltip(marker, fight);
      const start = clamp(fight.start, 0, length), end = clamp(fight.end, 0, length);
      const size = Math.min(width, Math.max(32, (end - start) / length * width));
      marker.style.width = `${size}px`;
      marker.style.left = `${clamp(start / length * width, 0, width - size)}px`;
      marker.addEventListener("click", () => jumpToFight(fight));
      markers.push(marker);
      const entry = document.createElement("button");
      entry.type = "button"; entry.className = "fight-entry";
      entry.textContent = label; entry.title = "";
      entry.setAttribute("aria-label", detail);
      entry.dataset.fight = String(fight.number);
      entry.dataset.outcome = fight.outcome;
      attachFightTooltip(entry, fight);
      const result = document.createElement("span"); result.className = "fight-entry-result";
      result.textContent = fightResult(fight);
      const people = document.createElement("span");
      people.className = "fight-participants"; people.textContent = fight.participants;
      entry.append(result, people); entry.addEventListener("click", () => jumpToFight(fight));
      entries.push(entry);
    }
    $("fightMarkers").replaceChildren(...markers);
    if (!count) {
      $("fightMarkers").textContent = !length && state.fights.length ? "Loading fight positions…" :
        state.fights.length ? "No detected fights within this video." : note;
      closeFightList();
    }
    const heading = document.createElement("div");
    heading.className = "fight-list-heading";
    heading.textContent = "Kill score: allies–enemies · green / red: share of kills · gray: unknown";
    $("fightList").replaceChildren(heading, ...entries);
    state.activeFight = null;
    highlightFight();
  }
  function highlightFight() {
    const seconds = state.media?.video ? video.currentTime : -1;
    const current = state.visibleFights.find(f => seconds >= Math.max(0, f.start) && seconds < f.end)?.number ?? null;
    if (current === state.activeFight) return;
    state.activeFight = current;
    for (const element of document.querySelectorAll("[data-fight]")) {
      if (Number(element.dataset.fight) === current) element.setAttribute("aria-current", "true");
      else element.removeAttribute("aria-current");
    }
  }
  function closeObjectiveList(returnFocus = false) {
    const open = !$("objectiveList").hidden;
    $("objectiveList").hidden = true;
    $("objectiveListToggle").setAttribute("aria-expanded", "false");
    if (open && returnFocus) $("objectiveListToggle").focus();
  }
  function prepareObjectiveCalls(calls) {
    const ids = new Set();
    state.objectiveCalls = (Array.isArray(calls) ? calls : []).filter(c => c && typeof c.id === "string" && c.id.length > 0 &&
      finite(c.start) && c.start >= 0 && finite(c.end) && c.end >= c.start && typeof c.label === "string" && c.label.trim() &&
      !ids.has(c.id) && ids.add(c.id)).map(c => ({ ...c, label: c.label.slice(0, 120),
        speaker: typeof c.speaker === "string" && c.speaker.trim() ? c.speaker : "Unassigned",
        champion: typeof c.champion === "string" ? c.champion : "",
        role: ["top", "jungle", "mid", "adc", "support", "coach", "overlap"].includes(c.role) ? c.role : "unknown"
      })).sort((a, b) => a.start - b.start);
    state.objectiveRevision++; state.activeObjectiveKey = "";
    if (!state.objectiveCalls.some(c => c.id === state.selectedObjective)) state.selectedObjective = "";
    closeObjectiveList(); renderObjectiveTimeline(); highlightObjectiveCall();
  }
  const objectiveVideoTime = cue => cue.start + state.captionAudioShift + state.offset + (state.media?.video ? totalAudioOffset() : 0);
  function jumpToObjective(cue) {
    hideFightTooltip(); closeFightList();
    state.selectedObjective = cue.id;
    seekComms(cue.start);
    closeObjectiveList(true);
    highlightObjectiveCall();
  }
  function renderObjectiveTimeline() {
    const shown = timelineVisible("calls") && hasMedia() && state.objectiveCalls.length > 0;
    $("objectiveTimeline").hidden = !shown;
    if (!shown) {
      if (state.objectiveLayoutKey === "hidden") return;
      state.visibleObjectives = []; state.objectiveLayoutKey = "";
      $("objectiveMarkers").replaceChildren(); $("objectiveList").replaceChildren();
      $("objectiveCount").textContent = ""; $("objectiveBanner").hidden = true;
      $("objectiveLabel").textContent = ""; $("objectiveBy").textContent = ""; $("objectivePortrait").replaceChildren();
      closeObjectiveList(); state.activeObjectiveKey = ""; state.objectiveLayoutKey = "hidden"; return;
    }
    const length = duration(), width = $("objectiveMarkers").clientWidth;
    const key = `${state.objectiveRevision}:${length}:${width}:${state.captionAudioShift}:${state.offset}:${state.media?.audioOffset}:${state.audioCorrection}`;
    if (key === state.objectiveLayoutKey) return;
    state.objectiveLayoutKey = key;
    state.visibleObjectives = length > 0 ? state.objectiveCalls.filter(c => objectiveVideoTime(c) >= 0 && objectiveVideoTime(c) < length) : [];
    $("objectiveCount").textContent = String(state.visibleObjectives.length);
    $("objectiveCount").title = `${state.visibleObjectives.length} of ${state.objectiveCalls.length} objective calls in this recording`;
    $("objectiveListToggle").disabled = state.visibleObjectives.length === 0;
    $("objectiveMarkers").classList.toggle("objective-empty", !state.visibleObjectives.length);
    const markers = [], entries = [];
    for (let index = 0; index < state.visibleObjectives.length; index++) {
      const cue = state.visibleObjectives[index];
      const description = `${cue.label} · ${cue.speaker} · ${time(objectiveVideoTime(cue))}`;
      const marker = document.createElement("button"); marker.type = "button"; marker.className = "objective-marker";
      marker.title = ""; marker.dataset.objective = cue.id; marker.setAttribute("aria-label", "Objective call: " + description);
      const position = clamp(objectiveVideoTime(cue) / length * width, 3, Math.max(3, width - 18));
      const next = state.visibleObjectives[index + 1];
      const nextPosition = next ? clamp(objectiveVideoTime(next) / length * width, 3, Math.max(3, width - 18)) : width;
      const markerWidth = Math.max(17, Math.min(170, nextPosition - position - 4, width - position - 2));
      marker.style.left = `${position}px`; marker.style.width = `${markerWidth}px`;
      const symbol = document.createElement("span"); symbol.className = "objective-symbol"; symbol.setAttribute("aria-hidden", "true");
      const inline = document.createElement("span"); inline.className = "objective-inline-label";
      inline.textContent = cue.label; inline.hidden = markerWidth < 60;
      marker.append(symbol, inline);
      marker.addEventListener("click", () => jumpToObjective(cue)); markers.push(marker);
      const entry = document.createElement("button"); entry.type = "button"; entry.className = "objective-entry";
      entry.dataset.objective = cue.id; entry.setAttribute("aria-label", description);
      const at = document.createElement("span"); at.className = "objective-entry-time";
      at.textContent = `${time(objectiveVideoTime(cue))} · ${cue.speaker}`;
      const label = document.createElement("strong"); label.textContent = cue.label;
      entry.append(at, label); entry.addEventListener("click", () => jumpToObjective(cue)); entries.push(entry);
    }
    $("objectiveMarkers").replaceChildren(...markers);
    if (!markers.length) {
      $("objectiveMarkers").textContent = !length ? "Loading call positions…" : "No calls within this recording.";
      closeObjectiveList();
    }
    const heading = document.createElement("div"); heading.className = "fight-list-heading";
    heading.textContent = "Objective calls · recording time · click to jump";
    $("objectiveList").replaceChildren(heading, ...entries);
    state.activeObjectiveKey = "";
  }
  function highlightObjectiveCall() {
    const shift = state.captionAudioShift + state.offset + (state.media?.video ? totalAudioOffset() : 0);
    const at = hasMedia() ? master().currentTime - shift : -Infinity;
    const active = state.objectiveCalls.filter(c => at >= c.start - .10 && at < Math.max(c.start + 5, c.end + 2));
    const cue = active.find(c => c.id === state.selectedObjective) || active[active.length - 1];
    if (!active.some(c => c.id === state.selectedObjective)) state.selectedObjective = "";
    const key = cue ? JSON.stringify([cue.id, cue.label, cue.speaker, cue.role, cue.champion, cue.icon]) : "";
    if (key === state.activeObjectiveKey) return;
    state.activeObjectiveKey = key;
    $("objectiveBanner").hidden = !cue;
    $("objectiveLabel").textContent = cue?.label || "";
    $("objectiveBy").textContent = cue ? cue.speaker + (cue.champion ? " · " + cue.champion : "") : "";
    $("objectiveBanner").dataset.role = cue?.role || "unknown";
    $("objectivePortrait").replaceChildren(...(cue ? [callerPortrait({ ...cue, label: cue.speaker })] : []));
    for (const element of document.querySelectorAll("[data-objective]")) {
      if (cue && element.dataset.objective === cue.id) element.setAttribute("aria-current", "true");
      else element.removeAttribute("aria-current");
    }
  }
  function setVolume() {
    audio.volume = state.volume; audio.muted = state.muted;
    video.volume = state.volume; video.muted = Boolean(state.media?.audio) || state.muted;
    $("mute").textContent = state.muted ? "Muted" : "Sound";
    $("mute").setAttribute("aria-pressed", String(state.muted));
  }
  function closeTakenList(returnFocus = false) {
    const open = !$("takenList").hidden;
    $("takenList").hidden = true;
    $("takenListToggle").setAttribute("aria-expanded", "false");
    if (open && returnFocus) $("takenListToggle").focus();
  }
  function prepareTaken(events, note) {
    const ids = new Set();
    state.objectiveEvents = (Array.isArray(events) ? events : []).filter(e => e && typeof e.id === "string" && e.id &&
      finite(e.start) && finite(e.gameTime) && e.gameTime >= 0 && typeof e.label === "string" && e.label.trim() &&
      !ids.has(e.id) && ids.add(e.id)).map(e => ({ ...e, label: e.label.slice(0, 120),
        side: ["ally", "enemy", "blue", "red"].includes(e.side) ? e.side : "unknown" })).sort((a, b) => a.start - b.start);
    state.takenStatus = typeof note === "string" ? note : "Recorded captures from saved live game events.";
    state.takenRevision++; state.takenLayoutKey = ""; state.activeTaken = "";
    closeTakenList(); renderTakenTimeline(); highlightTaken();
  }
  const takenSide = e => ({ ally: "Allies", enemy: "Enemies", blue: "Blue team", red: "Red team", unknown: "Team unknown" }[e.side]);
  function jumpToTaken(event) {
    hideFightTooltip(); closeFightList(); closeObjectiveList();
    // These are video coordinates. Never pass them through seekComms or any voice correction.
    seekMedia(Math.max(0, event.start)); closeTakenList(true);
  }
  // Riot's shipped minimap icons, sourced from CommunityDragon and embedded for offline use.
  const objectiveIcons = {"dragon":"data:image/png;base64,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","elder":"data:image/png;base64,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","baron":"data:image/png;base64,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","herald":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAO0UlEQVR4nO2af3BU13XHP+/te6srLYtX/BACCUmLxCJRZgwOiSMZTLJp7JRAFUuD4yZ26xnXMzTueOqQSW2XqRuPOnbdunQ69tSTyKGJacKEgq3iUNNOZGMo2FPF2B2MzC8jhIhkYWU1rFbvWW/3vf6x763evv2hlSHtH9F35szuu/fcc88579z77o8Dc5jDHOYwhzncUGiaJrkeJUAavjosFeJ3I3Y1lsXnkXXDccOFa5omlZeXA7Bnzx5pUp/M1G1t32pVL6pGkiSrBH2k3d27UYVKZ2enVV5ejqZplJeXF2r7/wMxT2TIsiwAed3adXJDQ4McCoXkqsVVwqFwQ1h03N0hNENDMzRQQAiBpmlomoYQogwoA8pCi0JloUWhssjKiBxZGZEBeccjO+Qbrf91R4CYJ9DiGgDlwXKlelG1OT4+XgcwPj5OoCIQSEwmCFQEAC4kphIC0G1CKMIRVQYIXdd1oNHVRSaEIisjg2fPnZU1TUs6UXa9uCEO6LirQznw8gGAcn1CX2hX1du/N7nYL6CQAMaxnaDFNSorK8sAAYRSZioANBpTBqpfxZgyrtltL9m/Y4AGJO0hcV36K9fVGqSOuzp8B14+oAAqsIBpwzcC9wKr7OfDNr1lP493dHRw//33W9jGA9XGlNEGfAtoMqYMgDPAHlefFvBrwNi/f3/SsqxUkTnlNwdN09izZ48ClIVCoTJVVVerqrqVtNGHbUW9dBX4EyHEKuyxHm4IO+O+mbThVwu0fQ34JrAaWB2YFygLN4XLhCIUzdAQinAPp5LxqYaAptljPh1/pqqqjYZhNAJNwB8BNxdpvg/4oRDiYigUGjBTJolEIpyYTISBLuCWIm3fA34EnAcuBOYFLqT0lAQkbUJP6rOy5VPNqvv371fKy8sVoBxwG/8tihsPEAXQdZ30fJeF8Axtb7b7aAIaExOJRluHT41ZO0DTNO69917sjuerquo2vsnNW1NZx8N3PFpQ1vj4eIP99vPWP3zHo9RU1nmL3X01AkFAtHe0KzEtNltzSnCAMk2WZUlP/c1TMgomClUorDQMI0Q67LOMjzZvZt/2N5jQx70SE7IsN6mq2qyqamDKmKpQVXWVbcxpN2MsMcq+7a8Rbb7TK8MZavP1pN6sJ/XqY8eOWeVquc+7kizFvJIhqRnZ7lC9F0/YR5s303XXcwCcvNTnFXMZWJ5KpTIFpmkuB5Z7Gd8bPAlA11272PnyI/R+cNhdfTNwH/D3ANfi18ItzS0XX+l5RSI9aZZm04wctossw5IkVZJt4WFgBfAlkvy5m91tPEDbUyu8Egft38vkMRrIivnjj00HRR4nADwD/CIYDH4Yj8cv2jYlZ7TLRslzgP32Vabffhh40M1TE6rLMr4A6my6zfXfTQXRddcuakI13uIHgXA8Hnd0UpiFXTMyCiW91P1659ctoQhVKEImSZgk3yDJAuf7KxTBQ5seRTV9GYpNjJSqR0HEJsZRzUCGHol+D3efQCXwDSAshJCrl1b7Sb+oklCyp3p6evxAWSqVqpdluZ7pFR8Am1beyRciX8lqE1CDpYovGdFVXyW6arO3+HagXtf1+pHhkbLZyCtpEqysrIT0as2x6Hagwc3zxJZdOe0C6vycsujKzUQjOQYA0Hv2EL3nDnlk5Dqx6/efp+1vc5YM98my3GWa5vzqpdWf3LL+lk8OHTyU09aLkhzQ3t7u7+npKQPmG4ZRT9oBGWxamfOZyqA1HOXExV52fKmLthVRls6rLsgbjWwmNjHC0Q97efoXO9kQjuZ1IkB01WZ6z2QZeDvpqPwYuDb8q+Ey4JOZbJvxK6DFNcmOgDqgXk/q7cC3nXqhCH7+4DsZ/nKRvR4/PdLPwvKFLCxPbxIV2Ve0v6Q5BcCV+CBX4oO0NUSz9XGtHjt3tzHuWmfIsvwPftn/Cumd4yUAPakX/STOOAfYxjtb2hDwRXf9hvDvZvGnkqksiiyIZIyfDWqCdXxu2YYceW60epxjmuZal6437fzLnTP2M5ulcMgWHnIXbljx5SwmR8l8Cn8aFJPnjQ5grUtPTp8+7a3PwYxzQHt7Oz09Pc5jCM+GZaPHAW6l8+H4pSMZgumdzK31m7i1fhOfWd5asry2cI4DQrheUP/p/oJ6OCjqAMuypBXhFX6f37cwMZkIARF3fbgykrPmSmFmPSdch6KdP20ruF29OH6Rve/9M0IR7P+D45lyIYrv8RsrI1yInc0860l9BfAusODk/5zUmGEiLDoEnJMW126t1l0froxkvR3vm3I/d/60rVhXWXDzukM/329TZcTbvBZmdpyD6zplDS9YlVdJ73j9u2P5J6Mdt3Wx47auvHXeNl65zm/TglU5bR1UV1c7J9UFMdszwVpvQSkT3YnLvXnL2+rSY/jZ/yrcplhUFdPROWyRpOJf+qIRYFmWlNATBOYFMoLdCCiVmLqZTcls0id09OQ0Odjx+a7MQdaOz09HgZtXn9Bz5Hn7E0pllk5CESmhiKRQRFKoYsZdYWlzwEQCYGgmYbNB2/Jo3v//15jxKyBJEoF5AccJWXh7qJegLGirmTaglCHRWptrcGttlBNDuUPFK8/H9Ery6OVejg/1DgMOjQDvY7+spcuWMnB5oKguRR0gSZIVbgozOjLqFA0B652Ht4d6eW/kOM8COz7XRVtNNK8DWmuinLgybVw+Q71lrbZTcxyg+Dh6uZen33ImSR3gJPAOaQeMAFQvrb6ydNnSYuYBpZ0HWImJhAmkgGNA3F3vjNe/Pv4dnnjzESZ1M4vQoW1JdPps0cbed3eRmhxCn9B45uhj7v4QiuDWJbej61qOvGdO7OTpt76D63ZtKVAly/JiWZan9KSeUitUa+DyAPZtVdFZsKADLMOSLMOSAILBzJZ0SJblfy/Upu+jI7w9nPt225ZEaV2SHfb7zu3L/H975EhOm88u2ZhT9t8fvcGRobxb3K2maX6GtDMACM4P0rKmhe//0/cLqQsUcYCkStx9z92+/g+ylpO1pmk2FWoD8PZIgU/ektxxv+/sPv7x5JM55S9++ed5ZfR9lOsoF7aaprkVWBa/Fl8WvxZf1n+qn4OvHvQNXx6WKBAJeQvtA1CJ9J3dEqDGpoeBnCXd+iWbspT7l99zLWVdfMc/6uXZd4rv0P7slr/yvP3pe48H/jOa+ZRuqt1cKBr+A3gB+JVNvyY9VvLOzjkRIBTByMSIJRThE4oIqKpaIctyEHiIPMZvb36U7Su/m1W2q+8xNF1D0zVi8ViGWirW0bWmO/tMz0Vda7pZU/FZtLg+TbacXX2PZa0j7mt4iO3NeS9d7gAeARYCNwkhKtatXScLIfIuj/N+BcKLw5A+WPQbhlFH+lIyZ1B2re2mel562K1ftIG+j48B0Hf1CH2jR1hftYmxiY8z/AvnLQJgZ/NznL72Dv3xkxjAmuA61syfvhIcS7jahGrpGz1C39XpCFu/aIP9u5GutU3sfPePvaptBAZVVf0JEOv/oF8FjHy25p0DnuvOHG0H7N8N+Yx3w1HKwQvvP0nf6BHGJsYydHbkTKZ+9fxb6Kx5gHtqHsgy/uzoGcYSYxnqGz3CC+9nzxPevry6ODrbly8BgJ2P5x96eR1w6N8OqYAfQJZl8JwA39OwPa8wL154/0nGJsZK4s2HscmxHOMLIY9O9aZp5mPNQs4k6BonFcDSqampZaZp/gjXQUjLvHV0VD+QI+z5gS7Gk7l3AXcuvoemwJq0/BlVSuNM4hSvXt2bUx5SqnmoYfpt6vYZ4rMfftvLelGW5T80TXO4prZm+MrQlUkvA+SJACdhyYbh8/mQZfkNN0//xEkOjLxYoilw+Openh/YyfnEqRl5zyROcXB0b17jC+F4LOe6DOB1n88HrhyjfMiZBJ10NNKThtP4deALuKLAcYI7EhorWvjltcK3QYev7uXwVYhUrCFiRwTAWdsxZydndlBjRUvW8/HYYU7kOuCiLMuv2/+NK0NX8k6AMPN5wKTP5xuw/z9hmub38DjhtdH9fKWqE4CmQAu/vPZ6rhQPzk6eKsnYfGgKTDvgtdH9+fq7KMvybtM0B/x+/0DVkqrJW1tv5cC+A7PrSNM0qaOjQ0VhAQqNKNyBwuPkz9+xoos7rAca/sISivDWXSrUpgQa8JY91NBl3bn4noL8QojHhRB3CCEahRALAMWyrIL7gZkuRmQULNIXkJVAI0k2kr6Xry/acho/kWUZ0zRLugG2cVSW5UEA0zS/WWI/yLLc5ff7jwIXgBgQ03VdAgp+DmYaAiYgtaxsifWf6we4oKqqlUqlME2zFCfESO8gsQ2qw06CtJMiakm/hMu2k4ZkWT5HOofAcVQr6VyEYrgky/JLPp/vTeBDu18nX6booWBJZ4IdWzo48OqBWP+5fgzDGATeJB3a9+G5J3RDluQXkRj0+XxOUsQg8I5hGKiqWgtgGIakquoQgGma+Hy+Cbt8UFXVOtM0/xX4bt4O0nhTluWXTNO8ZJrmoM/nyxivaVqxvGSg9AwRaVvnNnnf/n1+kgRJr66qSG+UGoE77d8mm/886by+1wTiQ9IbkikAtUJV49fianB+MH2Hb6sXj8cNwBCKcGZsFVigJ3UnCetPmc5DOm/TYSHEBWDUpoSe1K9t69xm/Gzvz0xJlayZckVmlSIDIElShV3jOMHEndurZMbbeQCBGLWNn9r81c30X+i3+k/1qwA1y2u4cvkKLc0tYK/V1/zOGgngYM9BP+DXk3qVLc8x3r12OS+E8DnGA2xt36pnjIdZJMsUc4ArSwxgy9e2EGmOBIBAoCJQFqgIrA5UBFYDq1FY5SLFzipRh4eHS8rkFIogFo+xrXObKhShVi2uUkin2zrUArQEg8GWYDDYIoQICCECHR0dDA8Pg4Lk1vn6oXjIxo7HdrDla1twUl2rFlelU14VlIVVC5VIS0Rp3dCqdP+g25vSUpIDYvEYP37px0S/GFWqFldlKBgM+oPBoL9mWY2/pbnFL4Sgu7ubzHa3gL6/MdhRIQHytm3bfLu7d8v2szTbnL0ikEiHvgzITh83UP4c5jCHOcxhDr+N+F9N7hXySxdnKgAAAABJRU5ErkJggg==","grubs":"data:image/png;base64,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","atakhan":"data:image/png;base64,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","tower":"data:image/png;base64,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","inhibitor":"data:image/png;base64,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","nexus":"data:image/png;base64,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","dragon_cloud":"data:image/png;base64,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","dragon_infernal":"data:image/png;base64,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","dragon_mountain":"data:image/png;base64,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","dragon_ocean":"data:image/png;base64,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","dragon_hextech":"data:image/png;base64,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","dragon_chemtech":"data:image/png;base64,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"};
  function objectiveIcon(kind, label = "") {
    let key = kind;
    if (kind === "dragon") {
      const element = /(?:cloud|air)/i.test(label) ? "cloud" : /(?:infernal|fire)/i.test(label) ? "infernal" :
        /(?:mountain|earth)/i.test(label) ? "mountain" : /(?:ocean|water)/i.test(label) ? "ocean" :
        /hextech/i.test(label) ? "hextech" : /chemtech/i.test(label) ? "chemtech" : "";
      if (element) key = "dragon_" + element;
    }
    const img = document.createElement("img"); img.alt = ""; img.setAttribute("aria-hidden", "true");
    img.src = objectiveIcons[key] || objectiveIcons.dragon;
    return img;
  }
  function renderTakenTimeline() {
    const shown = timelineVisible("objectives") && Boolean(state.media?.video);
    $("takenTimeline").hidden = !shown;
    if (!shown) {
      state.visibleTaken = []; state.takenLayoutKey = "hidden";
      $("takenMarkers").replaceChildren(); $("takenList").replaceChildren(); $("takenCount").textContent = "";
      closeTakenList(); return;
    }
    const length = duration(), width = $("takenMarkers").clientWidth;
    const key = `${state.takenRevision}:${length}:${width}`;
    if (key === state.takenLayoutKey) return;
    state.takenLayoutKey = key;
    state.visibleTaken = length > 0 ? state.objectiveEvents.filter(e => e.start >= 0 && e.start < length) : [];
    $("takenCount").textContent = state.visibleTaken.length ? String(state.visibleTaken.length) : "—";
    $("takenTimeline").title = state.takenStatus;
    $("takenCount").title = `${state.visibleTaken.length} recorded captures in this video. ${state.takenStatus}`;
    $("takenListToggle").disabled = !state.visibleTaken.length;
    $("takenMarkers").classList.toggle("taken-empty", !state.visibleTaken.length);
    const groups = [], entries = [], markers = [];
    for (const e of state.visibleTaken) {
      const pos = clamp(e.start / length * width, 2, Math.max(2, width - 24));
      const prior = groups[groups.length - 1];
      // Close repeated grub/structure captures share one icon. The list below
      // retains every original event and its exact time/click target.
      if (prior && pos - prior.pos < 30) {
        prior.events.push(e); prior.lastPos = pos;
      } else groups.push({ events: [e], pos, lastPos: pos });
      const entry = document.createElement("button"); entry.type = "button"; entry.className = "taken-entry";
      entry.dataset.taken = e.id; entry.dataset.side = e.side;
      const at = document.createElement("span"); at.className = "taken-entry-time"; at.textContent = time(e.gameTime);
      const label = document.createElement("strong"); label.textContent = `${e.label} · ${takenSide(e)}`;
      entry.setAttribute("aria-label", `${label.textContent} · game ${time(e.gameTime)} · video ${time(e.start)}`);
      entry.append(at, label); entry.addEventListener("click", () => jumpToTaken(e)); entries.push(entry);
    }
    for (let i = 0; i < groups.length; i++) {
      const group = groups[i], e = group.events[0];
      const marker = document.createElement("button"); marker.type = "button"; marker.className = "taken-marker";
      marker.style.left = `${group.pos}px`; marker.dataset.side = group.events.every(x => x.side === e.side) ? e.side : "unknown";
      marker.dataset.takenIds = JSON.stringify(group.events.map(x => x.id));
      const text = group.events.map(x => `${x.label} · ${takenSide(x)} · ${time(x.gameTime)}`).join("; ");
      marker.title = `${text} · game ${time(e.gameTime)}`;
      marker.setAttribute("aria-label", `${marker.title}. Open Objectives for every event time.`);
      const symbol = document.createElement("span"); symbol.className = "taken-symbol"; symbol.dataset.kind = e.kind;
      symbol.setAttribute("aria-hidden", "true");
      symbol.append(objectiveIcon(e.kind, e.label));
      marker.append(symbol);
      if (group.events.length > 1) {
        const badge = document.createElement("span"); badge.className = "taken-group-count";
        badge.textContent = String(group.events.length); marker.append(badge);
        marker.setAttribute("aria-haspopup", "true");
      }
      marker.addEventListener("click", () => {
        if (group.events.length === 1) { jumpToTaken(e); return; }
        closeFightList(); hideFightTooltip(); closeObjectiveList();
        $("takenList").hidden = false; $("takenListToggle").setAttribute("aria-expanded", "true");
        const ids = new Set(group.events.map(x => x.id));
        for (const entry of $("takenList").querySelectorAll("[data-taken]")) entry.hidden = !ids.has(entry.dataset.taken);
        $("takenList").querySelector("button:not([hidden])")?.focus();
      }); markers.push(marker);
    }
    $("takenMarkers").replaceChildren(...markers);
    if (!markers.length) $("takenMarkers").textContent = length ? state.takenStatus || "No recorded objective captures." : "Loading capture positions…";
    const heading = document.createElement("div"); heading.className = "fight-list-heading";
    heading.textContent = "Objectives · game time · click to jump";
    $("takenList").replaceChildren(heading, ...entries); state.activeTaken = "";
  }
  function highlightTaken() {
    const at = state.media?.video ? video.currentTime : -Infinity;
    const active = new Set(state.visibleTaken.filter(e => at >= e.start && at < e.start + 4).map(e => e.id));
    const key = [...active].join("|"); if (key === state.activeTaken) return; state.activeTaken = key;
    for (const element of document.querySelectorAll("[data-taken],[data-taken-ids]")) {
      const ids = element.dataset.takenIds ? JSON.parse(element.dataset.takenIds) : [element.dataset.taken];
      if (ids.some(id => active.has(id))) element.setAttribute("aria-current", "true"); else element.removeAttribute("aria-current");
    }
  }
  function pause() {
    state.playing = false;
    state.playRevision++;
    holdPlayback();
    state.buffering = state.audioBuffering = false;
    if (state.frame !== null) cancelAnimationFrame(state.frame);
    state.frame = null;
    controls(); renderCaptions(true);
  }
  function unload(element) { element.pause(); element.removeAttribute("src"); element.load(); }
  function closeToneDetails() {
    $("toneDetails").hidden = true;
    $("toneToggle").setAttribute("aria-expanded", "false");
  }
  const toneVideoTime = seconds => seconds + state.toneAudioShift + (state.media?.video ? totalAudioOffset() : 0);
  const toneScore = value => Math.round(clamp(value, 0, 1) * 100);
  function toneDescription(cue) {
    return `${time(Math.max(0, toneVideoTime(cue.start)))}–${time(Math.max(0, toneVideoTime(cue.end)))} · Team Outlook ${toneScore(cue.valence)}/100` +
      ` · ${state.toneWindowSeconds}s audio context (estimated vocal tone)`;
  }
  function prepareTone(tone, audioShift) {
    state.tone = (Array.isArray(tone?.segments) ? tone.segments : []).filter(c => c && finite(c.start) && finite(c.end) &&
      c.end > c.start && finite(c.valence)).sort((a, b) => a.start - b.start);
    state.toneWindowSeconds = Number.isInteger(tone?.windowSeconds) && tone.windowSeconds >= 5 && tone.windowSeconds <= 60 ? tone.windowSeconds : 20;
    state.toneStatus = typeof tone?.status === "string" ? tone.status : "Select Transcribe to add team Outlook.";
    state.toneAudioShift = finite(audioShift) ? audioShift : 0;
    state.toneRevision++; state.toneLayoutKey = ""; state.activeTone = null;
    $("toneStatus").textContent = state.toneStatus;
    $("toneReadout").textContent = "Hover or focus a window for the team's measured Outlook. Click to listen.";
    $("toneHint").textContent = "0–100 · rolling audio context · line interpolates between measurements · click to seek";
    renderToneTimeline(); highlightTone();
  }
  function toneColor(value) {
    const v = clamp(value, 0, 1), low = [191, 121, 135], middle = [140, 158, 170], high = [94, 184, 162];
    const from = v < .5 ? low : middle, to = v < .5 ? middle : high, mix = v < .5 ? v * 2 : (v - .5) * 2;
    return `rgb(${from.map((x, i) => Math.round(x + (to[i] - x) * mix)).join(",")})`;
  }
  // Shape-preserving cubic interpolation keeps every observed high/low point
  // and cannot overshoot the neighboring measurements like a generic spline.
  function outlookCurve(points) {
    if (!points.length) return "";
    if (points.length === 1) return `M${points[0].x},${points[0].y}`;
    const slopes = points.slice(1).map((p, i) => (p.y - points[i].y) / (p.x - points[i].x));
    const tangents = points.map((p, i) => i === 0 ? slopes[0] : i === points.length - 1 ? slopes.at(-1) :
      slopes[i - 1] * slopes[i] <= 0 ? 0 : 2 / (1 / slopes[i - 1] + 1 / slopes[i]));
    for (let i = 0; i < slopes.length; i++) {
      if (slopes[i] === 0) { tangents[i] = tangents[i + 1] = 0; continue; }
      const a = tangents[i] / slopes[i], b = tangents[i + 1] / slopes[i], norm = a * a + b * b;
      if (norm > 9) { const factor = 3 / Math.sqrt(norm); tangents[i] = factor * a * slopes[i]; tangents[i + 1] = factor * b * slopes[i]; }
    }
    let d = `M${points[0].x},${points[0].y}`;
    for (let i = 0; i < points.length - 1; i++) {
      const a = points[i], b = points[i + 1], step = (b.x - a.x) / 3;
      d += ` C${a.x + step},${a.y + tangents[i] * step} ${b.x - step},${b.y - tangents[i + 1] * step} ${b.x},${b.y}`;
    }
    return d;
  }
  function outlookRuns(cues) {
    // Connect the team measurements across speech gaps. The connecting curve is
    // interpolation; no emotion scores are invented or stored for silence.
    return cues.length ? [cues] : [];
  }
  function svgElement(tag, attributes = {}) {
    const node = document.createElementNS("http://www.w3.org/2000/svg", tag);
    for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, String(value));
    return node;
  }
  function renderToneTimeline() {
    const shown = hasMedia() && timelineVisible("outlook"); $("toneTimeline").hidden = !shown;
    if (!shown) { closeToneDetails(); $("toneMarkers").replaceChildren(); $("toneNow").textContent = "—"; state.toneLayoutKey = ""; return; }
    const length = duration(), width = $("toneMarkers").clientWidth;
    const key = `${state.toneRevision}:${length}:${width}:${state.toneAudioShift}:${state.media?.audioOffset}:${state.audioCorrection}`;
    if (key === state.toneLayoutKey) return; state.toneLayoutKey = key;
    state.toneVisible = state.tone.filter(c => toneVideoTime(c.end) > 0 && toneVideoTime(c.start) < length)
      .map((c, id) => ({ ...c, id, plotTime: clamp(toneVideoTime((c.start + c.end) / 2), 0, length) }));
    $("toneBaseline").textContent = `Team Outlook · ${state.toneWindowSeconds}s audio context · fixed 0–100 scale`;
    $("toneMarkers").replaceChildren();
    $("toneMarkers").setAttribute("aria-valuemin", "0"); $("toneMarkers").setAttribute("aria-valuemax", String(length));
    if (!state.toneVisible.length || width <= 0 || length <= 0) {
      const empty = document.createElement("span"); empty.className = "tone-empty"; empty.textContent = "No team Outlook estimate"; empty.title = state.toneStatus;
      $("toneMarkers").append(empty); state.activeTone = null; return;
    }
    const height = 104, top = 9, span = 86, middle = top + span / 2;
    const chart = svgElement("svg", { viewBox: `0 0 ${width} ${height}`, preserveAspectRatio: "none", "aria-hidden": "true" });
    const defs = svgElement("defs"), gradient = svgElement("linearGradient", { id: "outlookGradient", gradientUnits: "userSpaceOnUse", x1: 0, y1: top, x2: 0, y2: top + span });
    for (const [offset, color] of [["0%", "#73d7ad"], ["50%", "#a5b5c9"], ["100%", "#f08d9e"]]) gradient.append(svgElement("stop", { offset, "stop-color": color }));
    defs.append(gradient); chart.append(defs);
    for (const value of [0, .5, 1]) {
      const y = top + (1 - value) * span;
      chart.append(svgElement("path", { d: `M0,${y} H${width}`, stroke: value === .5 ? "#718196" : "#405064", "stroke-opacity": .6, "stroke-dasharray": "3 5" }));
      const label = svgElement("text", { x: 3, y: y - 2, fill: "#8d9db2", "font-size": 9 }); label.textContent = String(value * 100); chart.append(label);
    }
    for (const run of outlookRuns(state.toneVisible)) {
      const points = run.map(c => ({ x: c.plotTime / length * width, y: top + (1 - clamp(c.valence, 0, 1)) * span }));
      const first = points[0], last = points.at(-1);
      const left = clamp(toneVideoTime(run[0].start), 0, length) / length * width;
      const right = clamp(toneVideoTime(run.at(-1).end), 0, length) / length * width;
      const curvePoints = [...(left < first.x ? [{ x: left, y: first.y }] : []), ...points,
        ...(right > last.x ? [{ x: right, y: last.y }] : [])];
      const d = outlookCurve(curvePoints);
      if (curvePoints.length > 1) {
        chart.append(svgElement("path", { d: `${d} L${right},${middle} L${left},${middle} Z`, fill: "url(#outlookGradient)", opacity: .18 }));
        chart.append(svgElement("path", { d, fill: "none", stroke: "url(#outlookGradient)", "stroke-width": 2.8, "stroke-linecap": "round", "stroke-linejoin": "round" }));
      }
      for (let i = 0; i < run.length; i++) {
        const cue = run[i], point = points[i];
        const dot = svgElement("circle", { cx: point.x, cy: point.y, r: cue.valence < .4 ? 2.8 : 1.8,
          fill: toneColor(cue.valence), "data-tone-id": cue.id, "data-low": String(cue.valence < .4) });
        const title = svgElement("title"); title.textContent = toneDescription(cue); dot.append(title); chart.append(dot);
      }
    }
    chart.append(svgElement("path", { id: "outlookPlayhead", d: `M0,0 V${height}`, stroke: "#ecf5ff", "stroke-width": 1, opacity: .75 }));
    $("toneMarkers").append(chart); state.activeTone = null;
  }
  function nearestOutlook(videoTime) {
    let nearest = null, distance = Infinity;
    for (const cue of state.toneVisible) {
      const d = Math.abs(cue.plotTime - videoTime); if (d < distance) { distance = d; nearest = cue; }
    }
    return nearest;
  }
  function highlightTone() {
    if (!hasMedia()) return;
    const now = master().currentTime, width = $("toneMarkers").clientWidth;
    const cursor = $("outlookPlayhead");
    if (cursor && duration() > 0) cursor.setAttribute("transform", `translate(${clamp(now / duration(), 0, 1) * width},0)`);
    $("toneMarkers").setAttribute("aria-valuenow", String(now));
    const cue = state.toneVisible.find(c => toneVideoTime(c.start) <= now && toneVideoTime(c.end) > now);
    const id = cue?.id ?? -1; if (state.activeTone === id) return; state.activeTone = id;
    $("toneNow").textContent = cue ? String(toneScore(cue.valence)) : "—";
    $("toneNow").title = cue ? toneDescription(cue) : "No measured comms audio at this time";
    $("toneMarkers").setAttribute("aria-valuetext", cue ? toneDescription(cue) : "No measured speech at this time");
    for (const dot of $("toneMarkers").querySelectorAll("[data-tone-id]")) dot.setAttribute("aria-current", String(Number(dot.dataset.toneId) === id));
  }
  function timelineVisible(name) { return state.timelineSelection.includes(name); }
  function closeTimelinePicker() { $("timelinePicker").hidden = true; $("timelineToggle").setAttribute("aria-expanded", "false"); }
  const goldText = value => `${value > 0 ? "+" : value < 0 ? "−" : ""}${Math.round(Math.abs(value)).toLocaleString()}`;
  function prepareGold(gold) {
    state.gold = (Array.isArray(gold?.samples) ? gold.samples : []).filter(c => c && finite(c.start) && finite(c.advantage) &&
      finite(c.allyGold) && c.allyGold >= 0 && finite(c.enemyGold) && c.enemyGold >= 0 && Math.abs(c.advantage - (c.allyGold - c.enemyGold)) < .01)
      .sort((a, b) => a.start - b.start);
    state.goldInterpolate = gold?.interpolate === true;
    state.goldMaxGap = finite(gold?.maxSampleGapSeconds) && gold.maxSampleGapSeconds > 0 ? gold.maxSampleGapSeconds : 15;
    state.goldStatus = gold?.status || "No complete team gold history was saved."; state.goldLayoutKey = "";
    renderGoldTimeline(); highlightGold();
  }
  function goldAt(at) {
    let low = 0, high = state.gold.length;
    while (low < high) { const mid = (low + high) >>> 1; if (state.gold[mid].start <= at) low = mid + 1; else high = mid; }
    const sample = low ? state.gold[low - 1] : null, next = state.gold[low];
    if (!sample || at - sample.start > state.goldMaxGap) return null;
    if (!state.goldInterpolate || Math.abs(at - sample.start) < .001) return sample;
    if (!next || next.start - sample.start > state.goldMaxGap) return null;
    const mix = (at - sample.start) / (next.start - sample.start);
    const allyGold = sample.allyGold + (next.allyGold - sample.allyGold) * mix;
    const enemyGold = sample.enemyGold + (next.enemyGold - sample.enemyGold) * mix;
    return { ...sample, start: at, gameTime: sample.gameTime + (next.gameTime - sample.gameTime) * mix,
      allyGold, enemyGold, advantage: allyGold - enemyGold, estimated: true };
  }

  function renderGoldTimeline() {
    const shown = Boolean(state.media?.video) && timelineVisible("gold"); $("goldTimeline").hidden = !shown;
    if (!shown) { state.goldLayoutKey = ""; return; }
    const length = duration(), width = $("goldMarkers").clientWidth, key = `${length}:${width}:${state.gold.length}`;
    if (state.goldLayoutKey === key) return; state.goldLayoutKey = key; $("goldTimeline").title = state.goldStatus;
    const samples = state.gold.filter(c => c.start >= 0 && c.start < length);
    const initial = goldAt(0); if (initial && !samples.some(c => c.start === 0)) samples.unshift({ ...initial, start: 0 });
    $("goldMarkers").replaceChildren();
    if (!samples.length || length <= 0 || width <= 0) { $("goldMarkers").textContent = "Team gold unavailable"; $("goldMarkers").title = state.goldStatus; return; }
    const max = Math.max(1, ...samples.map(c => Math.abs(c.advantage))), ns = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(ns, "svg"); svg.setAttribute("viewBox", `0 0 ${width} 38`); svg.setAttribute("preserveAspectRatio", "none"); svg.setAttribute("aria-hidden", "true");
    const zero = document.createElementNS(ns, "path"); zero.setAttribute("d", `M0,19 H${width}`); zero.setAttribute("stroke", "#697787"); zero.setAttribute("stroke-width", "1"); svg.append(zero);
    for (let i = 0; i < samples.length; i++) {
      const c = samples[i], x = c.start / length * width;
      if (state.goldInterpolate) {
        const next = samples[i + 1];
        if (!next || next.start - c.start > state.goldMaxGap) {
          svg.append(svgElement("circle", { cx: x, cy: 19 - c.advantage / max * 16, r: 1.8, fill: c.advantage >= 0 ? "#72d7a4" : "#ed8c9b" })); continue;
        }
        const pieces = [{ x, value: c.advantage }];
        const right = next.start / length * width;
        if (c.advantage * next.advantage < 0) pieces.push({ x: x + (right - x) * (-c.advantage / (next.advantage - c.advantage)), value: 0 });
        pieces.push({ x: right, value: next.advantage });
        for (let j = 0; j < pieces.length - 1; j++) {
          const a = pieces[j], b = pieces[j + 1], y1 = 19 - a.value / max * 16, y2 = 19 - b.value / max * 16;
          const color = a.value + b.value >= 0 ? "#72d7a4" : "#ed8c9b";
          svg.append(svgElement("path", { d: `M${a.x},19 V${y1} L${b.x},${y2} V19 Z`, fill: color, opacity: .25 }),
            svgElement("path", { d: `M${a.x},${y1} L${b.x},${y2}`, fill: "none", stroke: color, "stroke-width": 1.8 }));
        }
        continue;
      }
      const end = Math.min(length, samples[i + 1]?.start ?? length, c.start + state.goldMaxGap);
      if (end <= c.start) continue;
      const right = end / length * width, y = 19 - c.advantage / max * 16, color = c.advantage > 0 ? "#72d7a4" : c.advantage < 0 ? "#ed8c9b" : "#99a8bb";
      const fill = document.createElementNS(ns, "path"); fill.setAttribute("d", `M${x},19 V${y} H${right} V19 Z`); fill.setAttribute("fill", color); fill.setAttribute("opacity", ".25");
      const line = document.createElementNS(ns, "path"); line.setAttribute("d", `M${x},${y} H${right}`); line.setAttribute("stroke", color); line.setAttribute("stroke-width", "1.8");
      svg.append(fill, line);
    }
    $("goldMarkers").append(svg);
  }
  function highlightGold() {
    const cue = state.media?.video ? goldAt(video.currentTime) : null;
    $("goldNow").textContent = cue ? goldText(cue.advantage) : "—";
    $("goldNow").dataset.advantage = cue ? cue.advantage > 0 ? "ally" : cue.advantage < 0 ? "enemy" : "even" : "unknown";
    $("goldNow").title = cue ? `Allied gold advantage ${goldText(cue.advantage)}${cue.estimated ? " (interpolated between match frames)" : ""} · allies ${Math.round(cue.allyGold).toLocaleString()}, enemies ${Math.round(cue.enemyGold).toLocaleString()} · game ${time(cue.gameTime)}` : state.goldStatus;
  }
  function renderLoading() {
    if (state.mediaFailed) { $("mediaLoading").hidden = true; $("stage").setAttribute("aria-busy", "false"); return; }
    const preparing = !hasMedia() && /loading/i.test($("emptyTitle").textContent);
    const paired = state.media?.video && state.media?.audio;
    const target = paired ? video.currentTime - totalAudioOffset() : 0;
    const audioInRange = paired && target >= 0 && (!finite(audio.duration) || target < audio.duration);
    const seeking = hasMedia() && master().seeking;
    const initial = hasMedia() && state.loading && master().readyState < 2;
    const buffering = state.playing && (state.buffering || state.audioBuffering && (!paired || audioInRange));
    $("mediaLoading").hidden = !(preparing || initial || seeking || buffering);
    $("mediaLoadingText").textContent = seeking ? "Seeking…" : buffering ? "Buffering media…" : "Loading preview…";
    $("stage").setAttribute("aria-busy", String(!$("mediaLoading").hidden));
  }
  function clear(message) {
    pause();
    state.loading = false; state.mediaFailed = false;
    state.media = null; state.cues = []; state.prefixEnds = []; state.cardKey = "";
    state.fights = [];
    state.gold = []; state.goldLayoutKey = ""; state.toneVisible = []; closeTimelinePicker();
    state.tone = []; state.toneStatus = ""; state.toneLayoutKey = ""; state.activeTone = null;
    closeToneDetails();
    state.objectiveEvents = []; state.visibleTaken = []; state.takenStatus = ""; state.takenLayoutKey = ""; state.activeTaken = "";
    state.objectiveCalls = []; state.objectiveLayoutKey = ""; state.activeObjectiveKey = ""; state.selectedObjective = "";
    state.pendingSeek = null; state.captionStatus = ""; state.buffering = false;
    state.audioBlocked = false; state.alignAudio = false; state.lastAudioSeek = -Infinity;
    unload(video); unload(audio);
    $("title").textContent = "Video review";
    $("captions").replaceChildren();
    $("empty").hidden = false;
    $("emptyTitle").textContent = message || "Select a recording";
    $("emptyDetail").textContent = "Play the video with synchronized voice comms.";
    status(message || "Select a recording to play its video.");
    renderFightTimeline();
    controls();
  }
  function prepareCues(cues) {
    const nextBySpeaker = new Map(), prepared = [];
    for (const source of (Array.isArray(cues) ? cues : []).filter(c => finite(c.start) && finite(c.end) && c.end >= c.start)
      .sort((a, b) => a.start - b.start)) {
      const cue = { ...source, words: (source.words || []).filter(w => finite(w.start) && finite(w.end) && w.end >= w.start) };
      const previous = prepared[prepared.length - 1];
      // Join punctuation fragments from the same voice into one readable card.
      if (previous && previous.speaker === cue.speaker && Boolean(previous.overlappingSpeech) === Boolean(cue.overlappingSpeech) &&
          !previous.text.includes("[overlapping speech;") && !cue.text.includes("[overlapping speech;") &&
          cue.start - previous.end <= .25 &&
          cue.end - previous.start <= 4.5 && previous.text.length + cue.text.length < 130) {
        previous.end = Math.max(previous.end, cue.end);
        previous.text += " " + cue.text; previous.words.push(...cue.words);
      } else prepared.push(cue);
    }
    state.cues = prepared.map(c => ({ ...c,
      // Estimated/zero-length word timings are not karaoke timestamps. Show
      // those phrases together instead of making their text appear seconds late.
      revealWords: c.words.length > 0 && c.words.every(w =>
        ["whisper_dtw", "whisper_token"].includes(w.timingSource) && w.end > w.start + .005),
      displayEnd: Math.max(c.end + .6, c.start + 1.2) }));
    // Do not carry a solo attribution into an overlap, or an overlap label into
    // the next solo turn. A missing-text marker ends with its detected activity.
    for (let i = 0; i < state.cues.length; i++) {
      const cue = state.cues[i], next = state.cues[i + 1];
      if (next && cue.end <= next.start)
        cue.displayEnd = Math.min(cue.displayEnd, next.start);
      if (next && (cue.overlappingSpeech || next.overlappingSpeech))
        cue.displayEnd = Math.min(cue.displayEnd, Math.max(cue.end, next.start));
      if (cue.words.some(w => w.text.includes("[overlapping speech; words not recovered]")))
        cue.displayEnd = cue.end;
    }
    for (let i = state.cues.length - 1; i >= 0; i--) {
      const cue = state.cues[i], next = nextBySpeaker.get(cue.speaker);
      if (finite(next)) cue.displayEnd = Math.min(cue.displayEnd, next);
      nextBySpeaker.set(cue.speaker, cue.start);
    }
    let end = 0;
    state.prefixEnds = state.cues.map(c => (end = Math.max(end, c.displayEnd)));
    state.cardKey = ""; renderCaptions(true);
  }
  const totalAudioOffset = () => (state.media?.audioOffset || 0) + state.audioCorrection;
  function captionClock() {
    if (!hasMedia()) return -1;
    if (state.media.audio) {
      const target = state.media.video ? video.currentTime - totalAudioOffset() : audio.currentTime;
      if (target < 0 || video.seeking || state.alignAudio || audio.seeking || audio.readyState < 2 ||
          (finite(audio.duration) && target >= audio.duration)) return -1;
      // The audible media clock is authoritative, including buffering, rate
      // changes, and seeks. Corrections to audio move its captions with it.
      return audio.currentTime - state.captionAudioShift - state.offset;
    }
    return video.currentTime - totalAudioOffset() - state.captionAudioShift - state.offset;
  }
  function visibleText(cue, seconds) {
    if (!cue.revealWords) return cue.text;
    return cue.words.filter(w => w.start <= seconds + .001).map(w => w.text).join(" ");
  }
  function activeCues(t) {
    let lo = 0, hi = state.cues.length;
    while (lo < hi) { const mid = (lo + hi) >>> 1; if (state.cues[mid].start <= t) lo = mid + 1; else hi = mid; }
    const found = [], speakers = new Set();
    for (let i = lo - 1; i >= 0 && state.prefixEnds[i] > t; i--) {
      const cue = state.cues[i];
      if (cue.displayEnd <= t || speakers.has(cue.speaker)) continue;
      speakers.add(cue.speaker); found.push(cue);
      if (found.length === (window.innerHeight < 390 ? 2 : 3)) break;
    }
    return found.reverse();
  }
  function renderCaptions(force = false) {
    const t = captionClock();
    const visible = state.captions ? activeCues(t) : [];
    const key = visible.map(c => c.id + ":" + visibleText(c, t)).join("|");
    if (!force && key === state.cardKey) return;
    state.cardKey = key;
    const cards = visible.map(cue => {
      const card = document.createElement("article"); card.className = "speech";
      card.dataset.role = cue.role;
      card.title = cue.overlappingSpeech
        ? "Simultaneous speakers from saved timings; words are not individually attributed to either voice."
        : cue.speaker || "Unknown speaker";
      let portrait;
      if (cue.overlappingSpeech && cue.participants?.length >= 2) {
        portrait = document.createElement("div"); portrait.className = "portrait-pair";
        portrait.append(...cue.participants.slice(0, 2).map(callerPortrait));
      } else portrait = callerPortrait(cue);
      const content = document.createElement("div"), label = document.createElement("div"), text = document.createElement("div");
      label.className = "speaker";
      const name = document.createElement("strong"); name.textContent = cue.label + (cue.champion ? " · " + cue.champion : "");
      label.append(name);
      text.className = "speech-text"; text.textContent = visibleText(cue, t);
      content.append(label, text); card.append(portrait, content); return card;
    });
    $("captions").replaceChildren(...cards);
  }
  function callerPortrait(person) {
    const fallback = () => {
      const item = document.createElement("span"); item.className = "portrait fallback";
      item.textContent = person.role === "overlap" ? "2+" : person.role === "coach" ? "C" :
        person.role === "unknown" ? "?" : (person.label || "?").slice(0, 2).toUpperCase();
      item.setAttribute("aria-hidden", "true"); return item;
    };
    if (typeof person.icon !== "string" || !/^data:image\/(?:png|jpeg|webp);base64,/.test(person.icon)) return fallback();
    const item = document.createElement("img"); item.className = "portrait";
    item.alt = person.champion || person.label || "Caller"; item.src = person.icon;
    item.addEventListener("error", () => item.replaceWith(fallback()), { once: true });
    return item;
  }
  function holdPlayback() {
    // Transport intent is separate from the native paused state. A seek or
    // buffer underrun holds BOTH clocks without changing the Play/Pause button.
    if (!video.paused || !audio.paused || state.playPending.size) {
      state.transportRevision++;
      state.playPending.clear();
      video.pause(); audio.pause();
    }
  }
  function ensurePlaying(element) {
    if (!state.playing || !element.paused || state.playPending.has(element) || state.audioBlocked) return;
    const ticket = { media: state.media, revision: state.playRevision, transport: state.transportRevision };
    state.playPending.set(element, ticket);
    function failed(error) {
      if (state.media !== ticket.media || state.playRevision !== ticket.revision ||
          state.transportRevision !== ticket.transport || !state.playing) return;
      if (error.name !== "AbortError") {
        pause(); state.audioBlocked = true;
        status(element === audio ? "Voice audio could not play. Press Play to retry." :
          "Could not play this video. Check that the recording is complete and uses a supported codec.", true);
        post({ type: "error", message: error.message });
      }
    }
    try {
      Promise.resolve(element.play()).catch(failed).finally(() => {
        if (state.playPending.get(element) === ticket) state.playPending.delete(element);
      });
    } catch (error) {
      if (state.playPending.get(element) === ticket) state.playPending.delete(element);
      failed(error);
    }
  }
  function syncAudio(force = false) {
    if (!hasMedia()) return;
    const paired = Boolean(state.media.video && state.media.audio);
    if (force && paired) { state.alignAudio = true; holdPlayback(); }
    if (state.pendingSeek !== null) { holdPlayback(); return; }
    const media = master();
    let useAudio = false, drift = 0;
    if (paired) {
      const target = video.currentTime - totalAudioOffset();
      useAudio = target >= 0 && (!finite(audio.duration) || target < audio.duration);
      if (audio.ended && finite(audio.duration) && target >= audio.duration - .10) useAudio = false;
      // play() on an ended element restarts it at zero. If the audio reaches
      // its end ahead of the video, seek it under the barrier before resuming.
      if (useAudio && audio.ended) state.alignAudio = true;
      // The lead-in/lead-out of unequal recordings is a valid silent interval.
      const audioTarget = Math.max(0, finite(audio.duration) ? Math.min(target, audio.duration) : target);
      drift = audioTarget - audio.currentTime;
      const now = performance.now();
      if (useAudio && state.playing && !state.alignAudio && !video.seeking && !audio.seeking &&
          Math.abs(drift) > .20 && now - state.lastAudioSeek >= 1000) state.alignAudio = true;
      if (state.alignAudio) {
        holdPlayback();
        if (audio.readyState === 0) return;
        // Assign the latest target even if a previous seek is still in flight.
        // Ignoring a new seek while audio.seeking is true was the scrubbing race.
        if (Math.abs(drift) > .025) {
          state.lastAudioSeek = now;
          audio.playbackRate = state.rate;
          audio.currentTime = audioTarget;
          return;
        }
        if (video.seeking || audio.seeking) return;
        state.alignAudio = false;
      }
      if (useAudio && (audio.seeking || audio.readyState < 3)) { holdPlayback(); return; }
      if (!useAudio && !audio.paused) audio.pause();
    }
    if (!state.playing || media.seeking || media.readyState < 3) { holdPlayback(); return; }
    // Ready/seeked events only reconcile this one barrier. Neither stream can
    // advance while the other is seeking or needs more data.
    if (paired && useAudio) {
      const rate = state.rate * clamp(1 + drift * .10, .98, 1.02);
      if (Math.abs(audio.playbackRate - rate) > .001) audio.playbackRate = rate;
      ensurePlaying(audio);
    }
    ensurePlaying(media);
  }
  function tick() {
    state.frame = null;
    if (!state.playing || !hasMedia()) return;
    syncAudio(); controls(); renderCaptions();
    if (state.playing) state.frame = requestAnimationFrame(tick);
  }
  function togglePlay() {
    if (!hasMedia()) return;
    // Clicking while loading/buffering must pause the pending request too.
    if (state.playing) { pause(); return; }
    state.playRevision++;
    state.playing = true;
    state.audioBlocked = false;
    state.audioBuffering = false;
    if (master().ended) seekMedia(0);
    controls();
    if (state.frame === null) state.frame = requestAnimationFrame(tick);
    syncAudio(true); regularStatus();
  }
  function seekMedia(seconds) {
    if (!hasMedia() || !finite(seconds)) return;
    if (!duration()) { state.pendingSeek = Math.max(0, seconds); return; }
    state.pendingSeek = null;
    holdPlayback();
    master().currentTime = clamp(seconds, 0, duration());
    syncAudio(true); renderCaptions(true); controls();
  }
  function seekComms(seconds) {
    if (!finite(seconds) || !hasMedia()) return;
    seekMedia(seconds + state.captionAudioShift + (state.media.video ? totalAudioOffset() : 0) + state.offset);
  }
  function load(data) {
    clear("Loading video…");
    state.media = data; state.loading = true;
    data.audioOffset = finite(data.audioOffset) ? data.audioOffset : 0;
    state.fullscreen = Boolean(data.fullScreen);
    state.captionStatus = data.captionStatus || "";
    state.offset = finite(data.captionCorrection) ? clamp(data.captionCorrection, -30, 30) : 0;
    state.audioCorrection = finite(data.audioCorrection) ? clamp(data.audioCorrection, -30, 30) : 0;
    state.captionAudioShift = finite(data.captionAudioShift) ? data.captionAudioShift : 0;
    state.audioBuffering = false;
    $("title").textContent = data.title || "Video review";
    $("empty").hidden = Boolean(data.video);
    $("emptyTitle").textContent = "No video saved for this recording";
    $("emptyDetail").textContent = data.audio ? "Voice audio and timed comms are available. Press Play to listen." : "This recording has no saved video or voice audio.";
    setVolume();
    video.playbackRate = audio.playbackRate = state.rate;
    if (data.audio) { audio.src = data.audio; audio.load(); }
    if (data.video) { video.src = data.video; video.load(); }
    prepareCues(data.cues);
    prepareFights(data.fights);
    prepareObjectiveCalls(data.objectiveCalls);
    prepareTaken(data.objectiveEvents, data.objectiveEventStatus);
    prepareTone(data.tone, data.toneAudioShift);
    prepareGold(data.gold);
    if (finite(data.videoSeek)) seekMedia(data.videoSeek);
    else if (finite(data.seek)) seekComms(data.seek);
    regularStatus();
    if (!hasMedia()) status("No playable video or audio was saved for this game.");
    updateFullscreen(); controls();
  }
  // The desktop host waits for this acknowledgement before moving the browser.
  window.leagueReviewBeginMove = () => { state.movingHost = true; };
  function savePlayerPreferences() {
    post({ type: "playerPreferences", preferences: { rate: state.rate, volume: state.volume, muted: state.muted, captions: state.captions, previewCommsVersion: 1, timelines: state.timelineSelection } });
  }
  function restorePlayerPreferences(p) {
    if (!p || typeof p !== "object") return;
    state.rate = [.5, .75, 1, 1.25, 1.5, 2].includes(p.rate) ? p.rate : 1;
    state.volume = finite(p.volume) ? clamp(p.volume, 0, 1) : .8;
    state.timelineSelection = Array.isArray(p.timelines) ? p.timelines.map(t => t === "valence" ? "outlook" : t).filter(t => ["fights", "calls", "objectives", "gold", "outlook"].includes(t)) : ["fights", "calls", "objectives", "gold", "outlook"];
    for (const box of $("timelinePicker").querySelectorAll("input")) box.checked = timelineVisible(box.value);
    state.muted = p.muted === true;
    state.captions = p.previewCommsVersion >= 1 ? p.captions !== false : true;
    $("speed").value = String(state.rate); $("volume").value = String(state.volume);
    $("comms").setAttribute("aria-pressed", String(state.captions));
    video.playbackRate = audio.playbackRate = state.rate;
    setVolume(); renderCaptions(true); renderFightTimeline(); controls();
  }
  function updateFullscreen() {
    $("fullscreen").textContent = state.fullscreen ? "Return" : "Expand";
    $("fullscreen").setAttribute("aria-label", state.fullscreen ? "Exit fullscreen" : "Enter fullscreen");
  }
  function receive(data) {
    if (!data || typeof data !== "object") return;
    switch (data.type) {
      case "preferences": restorePlayerPreferences(data.preferences); break;
      case "hostMoved": state.movingHost = false; if (state.playing) syncAudio(); break;
      case "load": load(data); break;
      case "clear": clear(data.message); break;
      case "captions":
        state.captionStatus = data.captionStatus || "";
        if (finite(data.captionAudioShift)) state.captionAudioShift = data.captionAudioShift;
        prepareCues(data.cues);
        if (Array.isArray(data.objectiveCalls)) prepareObjectiveCalls(data.objectiveCalls);
        prepareTone(data.tone, data.toneAudioShift);
        renderObjectiveTimeline(); highlightObjectiveCall(); regularStatus(); break;
      case "gold": if (state.media) { prepareGold(data.gold); } break;
      case "fights": if (state.media) { state.media.fightStatus = data.fightStatus || ""; prepareFights(data.fights); } break;
      case "pause": pause(); break;
      case "seek": seekComms(data.seconds); break;
      case "fullscreen": hideFightTooltip(); state.fullscreen = Boolean(data.value); updateFullscreen(); renderCaptions(true); break;
    }
  }
  $("timelineToggle").addEventListener("click", () => {
    const open = $("timelinePicker").hidden; closeFightList(); closeObjectiveList(); closeTakenList(); closeToneDetails(); hideFightTooltip();
    $("timelinePicker").hidden = !open; $("timelineToggle").setAttribute("aria-expanded", String(open));
  });
  for (const box of $("timelinePicker").querySelectorAll("input")) box.addEventListener("change", () => {
    state.timelineSelection = [...$("timelinePicker").querySelectorAll("input:checked")].map(e => e.value);
    state.fightLayoutKey = state.objectiveLayoutKey = state.takenLayoutKey = state.toneLayoutKey = state.goldLayoutKey = "";
    renderFightTimeline(); controls(); savePlayerPreferences();
  });
  $("toneMarkers").addEventListener("pointermove", e => {
    const rect = e.currentTarget.getBoundingClientRect(), at = clamp((e.clientX - rect.left) / Math.max(1, rect.width), 0, 1) * duration();
    const cue = nearestOutlook(at); const text = cue ? toneDescription(cue) + " · nearest measured window" : state.toneStatus;
    $("toneHint").textContent = text; $("toneReadout").textContent = text;
  });
  $("toneMarkers").addEventListener("pointerleave", () => { $("toneHint").textContent = "0–100 · rolling audio context · line interpolates between measurements · click to seek"; });
  $("toneMarkers").addEventListener("click", e => {
    const rect = e.currentTarget.getBoundingClientRect(); if (rect.width > 0) seekMedia(clamp((e.clientX - rect.left) / rect.width, 0, 1) * duration());
  });
  $("toneMarkers").addEventListener("keydown", e => {
    if (["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key)) {
      e.preventDefault(); seekMedia(e.key === "Home" ? 0 : e.key === "End" ? duration() : master().currentTime + (e.key === "ArrowLeft" ? -5 : 5));
    }
  });
  $("goldMarkers").addEventListener("click", e => {
    if (e.detail === 0) return; // Keyboard users seek with the arrow keys.
    const r = e.currentTarget.getBoundingClientRect();
    if (r.width > 0) seekMedia(clamp((e.clientX - r.left) / r.width, 0, 1) * duration());
  });
  $("goldMarkers").addEventListener("pointermove", e => {
    const r = e.currentTarget.getBoundingClientRect(), cue = goldAt(clamp((e.clientX - r.left) / Math.max(1, r.width), 0, 1) * duration());
    e.currentTarget.title = cue ? `${time(cue.gameTime)} game time · allied gold advantage ${goldText(cue.advantage)} · click to seek` : state.goldStatus;
  });
  $("goldMarkers").addEventListener("keydown", e => {
    if (e.key === "ArrowLeft" || e.key === "ArrowRight") { e.preventDefault(); seekMedia(master().currentTime + (e.key === "ArrowLeft" ? -5 : 5)); }
  });
  $("play").addEventListener("click", togglePlay);
  $("stage").addEventListener("dblclick", () => post({ type: "fullscreen" }));
  video.addEventListener("click", togglePlay);
  $("back").addEventListener("click", () => seekMedia(master().currentTime - 10));
  $("forward").addEventListener("click", () => seekMedia(master().currentTime + 10));
  $("seek").addEventListener("input", () => seekMedia(Number($("seek").value) / 1000 * duration()));
  $("speed").addEventListener("change", () => {
    state.rate = Number($("speed").value);
    video.playbackRate = audio.playbackRate = state.rate;
    syncAudio(true); savePlayerPreferences();
  });
  $("volume").addEventListener("input", () => { state.volume = Number($("volume").value); state.muted = false; setVolume(); savePlayerPreferences(); });
  $("mute").addEventListener("click", () => { state.muted = !state.muted; setVolume(); savePlayerPreferences(); });
  $("comms").addEventListener("click", () => { state.captions = !state.captions; $("comms").setAttribute("aria-pressed", String(state.captions)); renderCaptions(true); savePlayerPreferences(); });
  $("fightListToggle").addEventListener("click", () => {
    const open = $("fightList").hidden;
    $("fightList").hidden = !open;
    $("fightListToggle").setAttribute("aria-expanded", String(open));
    if (open) {
      closeTakenList(); closeObjectiveList();
      ($("fightList").querySelector("[aria-current='true']") || $("fightList").querySelector("button"))?.focus();
    }
  });
  document.addEventListener("click", e => {
    if (!e.target.closest("#timelinePicker,#timelineToggle")) closeTimelinePicker();
    if (!e.target.closest("#toneDetails,#toneToggle,.tone-segment")) closeToneDetails();
    if (!e.target.closest("#fightList,#fightListToggle,#fightTooltip")) closeFightList();
    if (!e.target.closest(".fight-marker,.fight-entry,#fightListToggle,#fightTooltip")) hideFightTooltip();
    if (!e.target.closest("#objectiveList,#objectiveListToggle")) closeObjectiveList();
    if (!e.target.closest("#takenList,#takenListToggle,.taken-marker")) closeTakenList();
  });
  $("objectiveListToggle").addEventListener("click", () => {
    const open = $("objectiveList").hidden;
    if (open) {
      closeTakenList(); closeFightList(); hideFightTooltip();
    }
    $("objectiveList").hidden = !open;
    $("objectiveListToggle").setAttribute("aria-expanded", String(open));
    if (open) ($("objectiveList").querySelector("[aria-current='true']") || $("objectiveList").querySelector("button"))?.focus();
  });
  $("takenListToggle").addEventListener("click", () => {
    const open = $("takenList").hidden;
    for (const entry of $("takenList").querySelectorAll("[data-taken]")) entry.hidden = false;
    if (open) { closeFightList(); hideFightTooltip(); closeObjectiveList(); }
    $("takenList").hidden = !open; $("takenListToggle").setAttribute("aria-expanded", String(open));
    if (open) ($("takenList").querySelector("[aria-current='true']") || $("takenList").querySelector("button"))?.focus();
  });
  $("fightList").addEventListener("scroll", hideFightTooltip);
  $("toneToggle").addEventListener("click", () => {
    const open = $("toneDetails").hidden;
    if (open) { closeFightList(); hideFightTooltip(); closeObjectiveList(); closeTakenList(); }
    $("toneDetails").hidden = !open; $("toneToggle").setAttribute("aria-expanded", String(open));
  });
  $("fightTooltip").addEventListener("pointerenter", () => {
    if (state.fightHoverTimer !== null) clearTimeout(state.fightHoverTimer);
    state.fightHoverTimer = null;
  });
  $("fightTooltip").addEventListener("pointerleave", scheduleFightTooltipHide);
  $("fullscreen").addEventListener("click", () => post({ type: "fullscreen" }));
  document.addEventListener("keydown", e => {
    if (e.key === "Escape") {
      if (!$("timelinePicker").hidden) { closeTimelinePicker(); $("timelineToggle").focus(); }
      else if (!$("toneDetails").hidden) { closeToneDetails(); $("toneToggle").focus(); }
      else if (!$("takenList").hidden) closeTakenList(true);
      else if (!$("objectiveList").hidden) closeObjectiveList(true);
      else if (!$("fightTooltip").hidden) hideFightTooltip();
      else if (!$("fightList").hidden) closeFightList(true);
      else if (state.fullscreen) post({ type: "fullscreen" });
      return;
    }
    if (e.target.closest("input,select,button,#toneMarkers")) return;
    if (e.code === "Space") { e.preventDefault(); togglePlay(); }
    else if (e.key === "ArrowLeft") { e.preventDefault(); seekMedia(master().currentTime - 5); }
    else if (e.key === "ArrowRight") { e.preventDefault(); seekMedia(master().currentTime + 5); }
    else if (e.code === "KeyF") post({ type: "fullscreen" });
  });
  document.addEventListener("visibilitychange", () => { if (document.hidden && !state.movingHost) { hideFightTooltip(); pause(); } });
  window.addEventListener("resize", () => { hideFightTooltip(); renderFightTimeline(); renderObjectiveTimeline(); highlightObjectiveCall(); renderTakenTimeline(); highlightTaken(); renderToneTimeline(); highlightTone(); renderCaptions(true); });
  new ResizeObserver(() => renderGoldTimeline()).observe($("goldMarkers"));
  new ResizeObserver(() => { renderToneTimeline(); highlightTone(); }).observe($("toneMarkers"));
  new ResizeObserver(() => { renderTakenTimeline(); highlightTaken(); }).observe($("takenMarkers"));
  new ResizeObserver(() => renderFightTimeline()).observe($("fightMarkers"));
  new ResizeObserver(() => { renderObjectiveTimeline(); highlightObjectiveCall(); }).observe($("objectiveMarkers"));
  for (const element of [video, audio]) {
    element.addEventListener("loadeddata", () => { if (element === master()) state.loading = false; controls(); renderCaptions(true); });
    element.addEventListener("loadedmetadata", () => {
      if (element === master() && state.pendingSeek !== null) seekMedia(state.pendingSeek);
      syncAudio(element === master()); renderFightTimeline(); controls(); renderCaptions(true);
    });
    element.addEventListener("durationchange", () => { if (element === master()) { renderFightTimeline(); controls(); } });
    element.addEventListener("timeupdate", () => {
      if (element === master()) controls();
      // The WAV clock drives speech, including audio readiness changes while
      // the video is paused. Do not wait for a video timeupdate to repaint it.
      if (element === audio || !state.media?.audio) renderCaptions();
    });
    element.addEventListener("seeking", () => {
      holdPlayback();
      if (element === video && state.media?.audio) state.alignAudio = true;
      syncAudio(); renderCaptions(true);
    });
    element.addEventListener("seeked", () => {
      syncAudio(); renderCaptions(true); controls();
    });
    element.addEventListener("waiting", () => {
      if (element === master() || (element === audio && state.media?.video &&
          video.currentTime - totalAudioOffset() >= 0 &&
          (!finite(audio.duration) || video.currentTime - totalAudioOffset() < audio.duration))) {
        holdPlayback();
        state.alignAudio = Boolean(state.media?.video && state.media?.audio);
      }
      if (element === video) state.buffering = true;
      else state.audioBuffering = true;
      controls(); renderCaptions(true);
    });
    element.addEventListener("canplay", () => {
      if (element === master()) state.loading = false;
      if (element === video) state.buffering = false;
      else state.audioBuffering = false;
      syncAudio(); controls(); renderCaptions(true);
    });
    element.addEventListener("playing", () => {
      if (!hasMedia() || !state.playing) {
        if (!element.paused) element.pause();
        controls(); return;
      }
      if (element === video) state.buffering = false;
      else state.audioBuffering = false;
      syncAudio(); controls();
    });
    element.addEventListener("ended", () => { if (element === master()) pause(); else syncAudio(); });
    element.addEventListener("error", () => {
      if (!element.getAttribute("src") || !hasMedia()) return;
      state.loading = false; state.mediaFailed = true; pause();
      if (element === audio) {
        state.audioBlocked = true;
      }
      status(element === video ? "Could not decode this video. Check that the MP4 recording is complete." : "Could not play the saved voice audio.", true);
      post({ type: "error", message: `${element === video ? "Video" : "Audio"} media error ${element.error?.code || "unknown"}` });
    });
  }
  window.chrome?.webview?.addEventListener("message", e => receive(e.data));
  clear("Select a recording to play its video.");
  post({ type: "ready" });
})();

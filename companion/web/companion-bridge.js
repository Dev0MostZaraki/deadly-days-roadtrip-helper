(() => {
  const webview = window.chrome?.webview;
  if (!webview) return;

  const send = payload => webview.postMessage(payload);
  let lastAutoKey = '';
  let liveGeometry = null;
  window.__DDR_COMPANION_SOURCES__ = null;

  function addControls() {
    const badges = document.querySelector('.badges');
    if (badges && !document.getElementById('companionLiveBadge')) {
      const badge = document.createElement('span');
      badge.id = 'companionLiveBadge';
      badge.className = 'badge';
      badge.textContent = 'Companion: verbunden';
      badges.appendChild(badge);

      const sources = document.createElement('span');
      sources.id = 'companionSourcesBadge';
      sources.className = 'badge';
      sources.textContent = 'Quellen: werden geprüft…';
      badges.appendChild(sources);
    }

    const actions = document.querySelector('.hero-actions');
    if (actions && !document.getElementById('companionScan')) {
      const scan = document.createElement('button');
      scan.id = 'companionScan';
      scan.textContent = 'Live scannen';
      scan.onclick = () => send({ type: 'companion.scan' });
      actions.prepend(scan);

      const learn = document.createElement('button');
      learn.id = 'companionLearn';
      learn.textContent = '3 Angebote lernen';
      learn.title = 'Korrektur/Training: Wenn die drei Airdrop-Auswahlen unten korrekt eingestellt sind, speichert der Companion lokale Bildreferenzen.';
      learn.onclick = () => {
        const selects = [...document.querySelectorAll('.candidate')];
        selects.forEach((s, slot) => send({ type: 'companion.learnCandidate', slot, itemId: s.value }));
      };
      actions.prepend(learn);
    }
  }

  function setBadge(text, warn = false) {
    const badge = document.getElementById('companionLiveBadge');
    if (!badge) return;
    badge.textContent = text;
    badge.classList.toggle('warn', warn);
  }

  function applySources(msg) {
    window.__DDR_COMPANION_SOURCES__ = msg;
    const badge = document.getElementById('companionSourcesBadge');
    if (!badge) return;
    const bits = [];
    if (msg.game?.found) bits.push(`Game✓${msg.game.buildId ? ` #${msg.game.buildId}` : ''}`);
    else bits.push('Game✗');
    bits.push(msg.save?.found ? 'Save✓' : 'Save✗');
    bits.push(msg.log?.found ? 'Log✓' : 'Log—');
    badge.textContent = `Quellen: ${bits.join(' · ')}`;
    badge.classList.toggle('warn', !msg.game?.found || !msg.save?.found || msg.game?.buildChanged === true);
    if (msg.game?.buildChanged) badge.title = 'Eine neue Spiel-Build-ID bzw. ein neuer Content-Fingerprint wurde erkannt. Spielabhängige Daten sollten erneut validiert werden.';
    else badge.title = msg.game?.installDirectory || '';
  }

  function applyDetectedRun(msg) {
    let changed = false;
    if (msg.characterId && typeof CHARACTERS !== 'undefined' && CHARACTERS[msg.characterId]) {
      state.character = msg.characterId;
      changed = true;
    }
    if (Array.isArray(msg.bagCells) && msg.bagCells.length) {
      state.bag = new Set(msg.bagCells);
      changed = true;
    }
    if (Array.isArray(msg.itemIds) && msg.itemIds.length) {
      state.items = [];
      state.nextInstance = 1;
      for (const itemId of msg.itemIds) {
        if (typeof ITEMS !== 'undefined' && ITEMS[itemId]) state.items.push(newInstance(itemId));
      }
      changed = true;
    }
    if (changed) {
      save();
      renderAll();
      if (liveGeometry) renderLiveGeometry(liveGeometry);
    }
  }

  function renderLiveGeometry(msg) {
    liveGeometry = msg;
    const grid = document.getElementById('layoutGrid');
    const score = document.getElementById('layoutScore');
    if (!grid || !score || !Array.isArray(msg.items) || !msg.items.length) return;

    const owner = {};
    for (const item of msg.items) {
      if (!item.itemId || !ITEMS[item.itemId] || !Array.isArray(item.cells)) continue;
      for (const c of item.cells) owner[c] = item.itemId;
    }

    const w = typeof GRID_W === 'undefined' ? 12 : GRID_W;
    const h = typeof GRID_H === 'undefined' ? 14 : GRID_H;
    grid.innerHTML = '';
    grid.style.setProperty('--s','28px');
    grid.style.gridTemplateColumns = `repeat(${w},var(--s))`;
    grid.style.gridTemplateRows = `repeat(${h},var(--s))`;
    grid.style.minHeight = '0';
    for (let y=0;y<h;y++) for (let x=0;x<w;x++) {
      const k = `${x},${y}`;
      const d = document.createElement('div');
      d.className = 'layout-cell' + (state.bag.has(k) ? ' bag' : '');
      const id = owner[k];
      if (id && ITEMS[id]) {
        const it = ITEMS[id];
        d.className += ' occupied ' + cellClass(it);
        d.innerHTML = `<span class="cell-label">${shortName(it.name)}</span>`;
        d.title = `LIVE: ${it.name}`;
      }
      grid.appendChild(d);
    }
    const conf = Number(msg.confidence || 0);
    score.innerHTML = `<strong>LIVE</strong><span>aktuelles erkanntes Layout · ${(conf*100).toFixed(0)}% Confidence</span>`;
  }

  function applyAirdrop(msg) {
    if (!Array.isArray(msg.airdropIds) || msg.airdropIds.length !== 3) return;
    const selects = [...document.querySelectorAll('.candidate')];
    let recognized = 0;
    msg.airdropIds.forEach((id, i) => {
      if (!id || !selects[i]) return;
      if ([...selects[i].options].some(o => o.value === id)) {
        selects[i].value = id;
        recognized++;
      }
    });

    const confidences = Array.isArray(msg.airdropConfidence) ? msg.airdropConfidence : [];
    if (recognized === 3) {
      const key = msg.airdropIds.join('|');
      const conf = confidences.length ? Math.min(...confidences) : 0;
      setBadge(`Airdrop erkannt · ${(conf * 100).toFixed(0)}%`);
      if (key !== lastAutoKey) {
        lastAutoKey = key;
        recommend();
        setTimeout(() => {
          const best = document.querySelector('.rec.best');
          if (!best) return;
          const name = best.querySelector('h4')?.textContent?.trim() || 'Empfehlung';
          const scoreText = best.querySelector('.rec-score')?.textContent || '';
          const score = Number.parseFloat(scoreText);
          send({ type: 'companion.overlayRecommendation', text: `Nimm: ${name}`, score: Number.isFinite(score) ? score : null });
        }, 0);
      }
    } else {
      setBadge(`Airdrop erkannt · ${recognized}/3 sicher`, true);
    }
  }

  webview.addEventListener('message', event => {
    const msg = event.data || {};
    if (msg.type === 'companion.detection') {
      setBadge(msg.status || (msg.gameRunning ? 'Companion: Spiel erkannt' : 'Companion: wartet auf Spiel'), !msg.gameRunning);
      applyDetectedRun(msg);
      applyAirdrop(msg);
    } else if (msg.type === 'companion.inventoryGeometry') {
      renderLiveGeometry(msg);
    } else if (msg.type === 'companion.sources') {
      applySources(msg);
    } else if (msg.type === 'companion.learnResult') {
      setBadge(msg.ok ? `Referenz gelernt: ${msg.itemId}` : 'Referenz konnte nicht gelernt werden', !msg.ok);
    }
  });

  window.addEventListener('DOMContentLoaded', () => {
    addControls();
    send({ type: 'companion.ready' });
  });
})();

(() => {
  const webview = window.chrome?.webview;
  if (!webview) return;

  const send = payload => webview.postMessage(payload);
  let lastAutoKey = '';

  function addControls() {
    const badges = document.querySelector('.badges');
    if (badges && !document.getElementById('companionLiveBadge')) {
      const badge = document.createElement('span');
      badge.id = 'companionLiveBadge';
      badge.className = 'badge';
      badge.textContent = 'Companion: verbunden';
      badges.appendChild(badge);
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
      learn.title = 'Wenn die drei Airdrop-Auswahlen unten korrekt eingestellt sind, speichert der Companion lokale Bildreferenzen.';
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

  function applyDetectedRun(msg) {
    let changed = false;
    if (msg.characterId && window.CHARACTERS?.[msg.characterId]) {
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
        if (window.ITEMS?.[itemId]) state.items.push(newInstance(itemId));
      }
      changed = true;
    }
    if (changed) {
      save();
      renderAll();
    }
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
      setBadge(msg.gameRunning ? 'Companion: Spiel erkannt' : 'Companion: wartet auf Spiel', !msg.gameRunning);
      applyDetectedRun(msg);
      applyAirdrop(msg);
    } else if (msg.type === 'companion.learnResult') {
      setBadge(msg.ok ? `Referenz gelernt: ${msg.itemId}` : 'Referenz konnte nicht gelernt werden', !msg.ok);
    }
  });

  window.addEventListener('DOMContentLoaded', () => {
    addControls();
    send({ type: 'companion.ready' });
  });
})();

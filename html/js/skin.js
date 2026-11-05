// Server pages: the Fleet skin (css/fleet-skin.css) is on when localStorage 'phoenix.skin' is 'fleet'. Default: Fleet (hackathon).
(function () {
  let skin = 'fleet';
  try { skin = localStorage.getItem('phoenix.skin') === 'phoenix' ? 'phoenix' : 'fleet'; } catch (e) {}
  const apply = () => { if (skin === 'fleet') document.documentElement.dataset.skin = 'fleet'; else delete document.documentElement.dataset.skin; };
  apply();
  document.addEventListener('DOMContentLoaded', () => {
    const b = document.getElementById('skinBtn');
    if (b) b.onclick = () => { skin = skin === 'fleet' ? 'phoenix' : 'fleet'; try { localStorage.setItem('phoenix.skin', skin); } catch (e) {} apply(); };
  });
})();

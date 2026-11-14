// Server pages: the Fleet skin (css/fleet-skin.css) is on when localStorage 'phoenix.skin' is 'fleet'.
// Phoenix is the default, same as the console's skin command and the client. The dashboard Skin button toggles it.
(function () {
  let skin = 'phoenix';
  try { const saved = localStorage.getItem('phoenix.skin'); if (saved === 'fleet' || saved === 'phoenix') skin = saved; } catch (e) {}
  const apply = () => { document.documentElement.dataset.skin = skin; };
  apply();
  document.addEventListener('DOMContentLoaded', () => {
    const b = document.getElementById('skinBtn');
    if (b) b.onclick = () => { skin = skin === 'fleet' ? 'phoenix' : 'fleet'; try { localStorage.setItem('phoenix.skin', skin); } catch (e) {} apply(); };
  });
})();

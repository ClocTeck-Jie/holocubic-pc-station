window.promptBox.init().then(d=>{document.querySelector('#message').textContent=d.message;const i=document.querySelector('#value');i.value=d.value;i.focus();i.select()});
document.querySelector('#accept').onclick=()=>window.promptBox.done(document.querySelector('#value').value);
document.querySelector('#cancel').onclick=()=>window.promptBox.done(null);
document.addEventListener('keydown',e=>{if(e.key==='Enter')window.promptBox.done(document.querySelector('#value').value);if(e.key==='Escape')window.promptBox.done(null)});

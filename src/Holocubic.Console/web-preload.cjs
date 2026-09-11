const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('__holoPrompt',(message,value)=>ipcRenderer.sendSync('web-prompt',{message:String(message).slice(0,2000),value:String(value??'').slice(0,4000)}));
contextBridge.executeInMainWorld({func:()=>{window.prompt=(message,value)=>window.__holoPrompt(message,value)}});

const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('promptBox',{init:()=>ipcRenderer.invoke('prompt-init'),done:value=>ipcRenderer.send('prompt-done',value)});

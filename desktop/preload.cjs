const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('deskbox', {
  saveApiKey: (key) => ipcRenderer.invoke('deskbox:save-api-key', key),
});

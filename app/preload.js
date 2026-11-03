const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  info: () => ipcRenderer.invoke('info'),
  settings: () => ipcRenderer.invoke('settings'),
  saveSettings: (patch) => ipcRenderer.invoke('save-settings', patch),
  status: () => ipcRenderer.invoke('status'),
  search: (q) => ipcRenderer.invoke('search', q),
  player: (id) => ipcRenderer.invoke('player', id),
  savePlayer: (id, changes) => ipcRenderer.invoke('save-player', id, changes),
  searchClubs: (q) => ipcRenderer.invoke('search-clubs', q),
  club: (id) => ipcRenderer.invoke('club', id),
  saveClub: (id, changes) => ipcRenderer.invoke('save-club', id, changes),
  saveSquad: (id, values) => ipcRenderer.invoke('save-squad', id, values),
  transferStatus: () => ipcRenderer.invoke('transfer-status'),
  transfer: (action, req) => ipcRenderer.invoke('transfer', action, req),
  openUrl: (url) => ipcRenderer.invoke('open-url', url),
  addonStatus: () => ipcRenderer.invoke('addon-status'),
  addonInstall: (folder) => ipcRenderer.invoke('addon-install', folder),
  addonOn: (on) => ipcRenderer.invoke('addon-on', on),
  addonRemove: () => ipcRenderer.invoke('addon-remove'),
});

import { app, BrowserWindow, ipcMain, safeStorage, session } from 'electron';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from '../server.mjs';

let server, window, appOrigin, apiKey = '';
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', () => { if (window?.isMinimized()) window.restore(); window?.focus(); });
  await app.whenReady();
  // Development may use .env; packaged applications use the in-app settings.
  if (!app.isPackaged) {
    try { process.loadEnvFile(fileURLToPath(new URL('../.env', import.meta.url))); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  const credentialPath = join(app.getPath('userData'), 'voice-credential.enc');
  const secureStorage = () => safeStorage.isEncryptionAvailable() && (process.platform !== 'linux' || safeStorage.getSelectedStorageBackend() !== 'basic_text');
  apiKey = process.env.OPENAI_API_KEY || '';
  if (!apiKey && secureStorage()) {
    try { apiKey = safeStorage.decryptString(await readFile(credentialPath)); }
    catch { /* Missing credentials or a changed OS keychain: prompt for a new key. */ }
  }
  server = createServer({ getApiKey: () => apiKey });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  appOrigin = `http://127.0.0.1:${server.address().port}`;
  ipcMain.handle('deskbox:save-api-key', async (event, key) => {
    if (event.sender !== window?.webContents || new URL(event.senderFrame.url).origin !== appOrigin) throw new Error('Untrusted request');
    if (typeof key !== 'string' || !/^sk-[A-Za-z0-9_-]{16,512}$/.test(key)) throw new Error('Invalid API key');
    const persisted = secureStorage();
    if (persisted) {
      await mkdir(app.getPath('userData'), { recursive: true });
      await writeFile(credentialPath, safeStorage.encryptString(key), { mode: 0o600 });
    }
    apiKey = key;
    return { configured: true, persisted };
  });
  session.defaultSession.setPermissionCheckHandler((contents, permission, origin) => contents === window?.webContents && permission === 'media' && origin === appOrigin);
  session.defaultSession.setPermissionRequestHandler((contents, permission, callback, details) => {
    const ownPage = contents === window?.webContents && new URL(contents.getURL()).origin === appOrigin;
    callback(ownPage && permission === 'media' && Boolean(details.mediaTypes?.length) && details.mediaTypes.every((type) => type === 'audio'));
  });
  session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
    if (!details.url.startsWith(`${appOrigin}/`)) return callback({ responseHeaders: details.responseHeaders });
    callback({ responseHeaders: { ...details.responseHeaders, 'Content-Security-Policy': ["default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; connect-src 'self' https://api.openai.com wss://api.openai.com; media-src 'self' blob: mediastream:; img-src 'self' data:; object-src 'none'; base-uri 'none'"] } });
  });
  function openWindow() {
    window = new BrowserWindow({
      width: 1460, height: 980, minWidth: 980, minHeight: 720,
      title: 'DeskBox · NOVA 全息通讯舱', backgroundColor: '#030910',
      autoHideMenuBar: true, show: false,
      webPreferences: { preload: fileURLToPath(new URL('./preload.cjs', import.meta.url)), contextIsolation: true, nodeIntegration: false, sandbox: true },
    });
    window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    window.webContents.on('will-navigate', (event, url) => { if (new URL(url).origin !== appOrigin) event.preventDefault(); });
    window.once('ready-to-show', () => window.show());
    window.on('closed', () => { window = null; });
    window.loadURL(appOrigin);
  }
  openWindow();
  app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) openWindow(); });
  app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit(); });
  app.on('before-quit', () => { server?.close(); server?.closeAllConnections(); });
}

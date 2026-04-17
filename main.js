const { app, BrowserWindow, ipcMain, desktopCapturer, shell, dialog, screen } = require('electron');
const fs = require('fs');
const path = require('path');

let mainWindow;
let toolbarBounds = { x: 0, y: 0, width: 0, height: 0 };
let isDrawingModeGlobal = true;

function createWindow() {
    const primaryDisplay = screen.getPrimaryDisplay();
    const { x, y, width, height } = primaryDisplay.bounds;

    mainWindow = new BrowserWindow({
        width: width,
        height: height,
        x: x,
        y: y,
        transparent: true,
        frame: false,
        alwaysOnTop: true,
        skipTaskbar: false,
        movable: false,
        resizable: false,
        focusable: true,
        webPreferences: {
            nodeIntegration: true,
            contextIsolation: false,
            backgroundThrottling: false
        }
    });

    mainWindow.loadFile('index.html');

    ipcMain.on('update-toolbar-state', (event, b, isDrawing) => {
        toolbarBounds = b;
        isDrawingModeGlobal = isDrawing;
        if (isDrawingModeGlobal && mainWindow) {
            mainWindow.setIgnoreMouseEvents(false);
        }
    });

    // RESOLUTION-AGNOSTIC POINTER GUARDIAN
    const pointerGuardian = setInterval(() => {
        if (!mainWindow || mainWindow.isDestroyed() || isDrawingModeGlobal) return;
        const point = screen.getCursorScreenPoint();
        const overToolbar = (
            point.x >= toolbarBounds.x && 
            point.x <= toolbarBounds.x + toolbarBounds.width &&
            point.y >= toolbarBounds.y && 
            point.y <= toolbarBounds.y + toolbarBounds.height
        );
        
        if (overToolbar) {
            mainWindow.setIgnoreMouseEvents(false);
        } else {
            // Forward: true allows drawing on the canvas while passing clicks to apps below
            mainWindow.setIgnoreMouseEvents(true, { forward: true });
        }
    }, 50);

    mainWindow.on('closed', () => {
        clearInterval(pointerGuardian);
        mainWindow = null;
    });

    ipcMain.on('select-folder', async (event) => {
        const result = await dialog.showOpenDialog(mainWindow, {
            properties: ['openDirectory']
        });
        if (!result.canceled) {
            event.reply('folder-selected', result.filePaths[0]);
        }
    });

    ipcMain.on('open-save-folder', (event, p) => {
        if (p && fs.existsSync(p)) {
            shell.openPath(p);
        }
    });

    ipcMain.on('get-background-capture', async (event) => {
        const win = BrowserWindow.fromWebContents(event.sender);
        win.hide();
        setTimeout(async () => {
            try {
                const disp = screen.getPrimaryDisplay();
                const sources = await desktopCapturer.getSources({ 
                    types: ['screen'], 
                    thumbnailSize: disp.size 
                });
                event.reply('background-captured', sources[0].thumbnail.toDataURL());
            } catch (e) {}
            win.show();
        }, 150);
    });

    ipcMain.on('capture-screen', async (event, region, customPath) => {
        try {
            const disp = screen.getPrimaryDisplay();
            const sources = await desktopCapturer.getSources({ 
                types: ['screen'], 
                thumbnailSize: disp.size 
            });
            let nativeImg = sources[0].thumbnail;

            if (region) {
                const imgSize = nativeImg.getSize();
                const clampX = Math.max(0, Math.min(region.x, imgSize.width - 1));
                const clampY = Math.max(0, Math.min(region.y, imgSize.height - 1));
                const clampW = Math.max(1, Math.min(region.width, imgSize.width - clampX));
                const clampH = Math.max(1, Math.min(region.height, imgSize.height - clampY));
                nativeImg = nativeImg.crop({ x: clampX, y: clampY, width: clampW, height: clampH });
            }

            const png = nativeImg.toPNG();
            const saveDir = (customPath && fs.existsSync(customPath)) ? customPath : app.getPath('desktop');
            const filePath = path.join(saveDir, `Simpink_Snap_${Date.now()}.png`);
            
            fs.writeFileSync(filePath, png);
            shell.showItemInFolder(filePath);
        } catch (e) {
            console.error("Global Capture Error:", e);
        }
    });
    ipcMain.handle('get-screen-source-id', async () => {
        const sources = await desktopCapturer.getSources({ types: ['screen'] });
        return sources[0].id;
    });

    ipcMain.on('get-desktop-path', (event) => {
        event.reply('desktop-path', app.getPath('desktop'));
    });
}

app.commandLine.appendSwitch('enable-gpu-rasterization');
app.commandLine.appendSwitch('disable-software-rasterizer');
app.commandLine.appendSwitch('ignore-gpu-blacklist');

app.whenReady().then(createWindow);
app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit();
});

const { createApp, ref } = Vue

let fileModal = null;
let mountedElement = null;
let initAttempts = 0;
const MAX_INIT_ATTEMPTS = 50; // 5 segundos máximo

function initFileUploadVue() {
    const mountElement = document.getElementById('modalFileUploadVue');

    // Si el elemento no existe, esperar
    if (!mountElement) {
        initAttempts++;
        if (initAttempts < MAX_INIT_ATTEMPTS) {
            setTimeout(initFileUploadVue, 100);
        } else {
            console.error('[FileUpload] No se encontró #modalFileUploadVue después de múltiples intentos');
        }
        return false;
    }

    // Si ya está inicializado en el mismo elemento, no hacer nada
    if (fileModal && mountedElement === mountElement) {
        console.log('[FileUpload] Vue ya inicializado en este elemento');
        return true;
    }

    // Si estaba montado en otro elemento, desmontar
    if (fileModal && mountedElement !== mountElement) {
        console.log('[FileUpload] Elemento cambió, desmontando Vue anterior...');
        try {
            fileModal.unmount();
        } catch (e) {
            console.log('[FileUpload] Error al desmontar:', e);
        }
        fileModal = null;
        mountedElement = null;
    }

    console.log('[FileUpload] Inicializando Vue en #modalFileUploadVue');
    initAttempts = 0;

    fileModal = createApp({
    setup() {
        const message = ref('Hello vue!');
        return {
            message,
        }
    },
    data() {
        return {
            files: [],
            showModal: false,
            showAudioModal: false,
            id: "",
            path: "",
            url: "",
            isDragging: false,
            isUploading: false,
            concurrentUploads: 3,
            activeUploads: 0
        };
    },
    methods: {
        onChangeFileUpload() {
            let filterFiles = [];
            filterFiles.push(...this.$refs.files.files);
            this.addFilesToList(filterFiles);
        },
        addFilesToList(filterFiles) {
            let listFiles = [...this.files];
            filterFiles.forEach(file => {
                // Avoid duplicates
                if (!listFiles.some(f => f.file.name === file.name && f.file.size === file.size)) {
                    const newFile = {
                        file: file,
                        progress: 0,
                        sended: 0,
                        total: this.bytes(file.size),
                        completed: false
                    };
                    listFiles.push(newFile);
                }
            });
            this.files = listFiles;
        },
        deleteFile(name) {
            this.files = this.files.filter(file => file.file.name !== name);
        },
        onDragOver(e) {
            this.isDragging = true;
        },
        onDragLeave(e) {
            this.isDragging = false;
        },
        onDrop(e) {
            this.isDragging = false;
            const droppedFiles = [...e.dataTransfer.files];
            this.addFilesToList(droppedFiles);
        },
        async submitForm() {
            if (this.files.length === 0 || this.isUploading) return;

            this.isUploading = true;
            this.activeUploads = 0;
            console.log(`Enviando ${this.files.length} archivos (${this.concurrentUploads} simultáneos)...`);

            // Get files that haven't been uploaded yet
            const pendingFiles = this.files.filter(f => !f.completed);

            // Upload with concurrency limit
            await this.uploadWithConcurrency(pendingFiles, this.concurrentUploads);

            this.isUploading = false;
        },

        async uploadWithConcurrency(files, limit) {
            const queue = [...files];
            const executing = [];

            const uploadFile = async (fileItem) => {
                this.activeUploads++;

                return new Promise((resolve) => {
                    let xhr = new XMLHttpRequest();
                    xhr.open("POST", this.url);
                    let data = new FormData();
                    data.set('file', fileItem.file);
                    data.set('id', this.id);
                    data.set('path', this.path);
                    data.set('action', 'save');

                    xhr.upload.addEventListener("progress", ({ loaded, total }) => {
                        fileItem.progress = Math.floor((loaded / total) * 100);
                        fileItem.sended = loaded;

                        if (loaded == total) {
                            fileItem.completed = true;
                        }
                    });

                    xhr.onloadend = () => {
                        this.activeUploads--;
                        resolve();
                    };

                    xhr.onerror = () => {
                        this.activeUploads--;
                        resolve();
                    };

                    xhr.send(data);
                });
            };

            while (queue.length > 0 || executing.length > 0) {
                // Start new uploads while under limit and queue has items
                while (executing.length < limit && queue.length > 0) {
                    const fileItem = queue.shift();
                    const promise = uploadFile(fileItem).then(() => {
                        executing.splice(executing.indexOf(promise), 1);
                    });
                    executing.push(promise);
                }

                // Wait for at least one to complete before continuing
                if (executing.length > 0) {
                    await Promise.race(executing);
                }
            }
        },
        closeModal() {
            const hadFiles = this.files.length > 0;
            this.files = [];
            this.showModal = false;
            this.isDragging = false;
            this.isUploading = false;

            // Notificar a Blazor para refrescar el FileManager si se subieron archivos
            if (hadFiles) {
                try {
                    DotNet.invokeMethodAsync('TelegramDownloader', 'RefreshFileManagerStatic').catch(() => {});
                } catch (e) {
                    console.log('[FileUpload] No se pudo refrescar FileManager:', e);
                }
            }
        },
        closeModalPlayer() {
            this.showAudioModal = false;
        },
        getFileIcon(fileName) {
            if (!fileName) return 'bi-file-earmark';

            const ext = fileName.split('.').pop()?.toLowerCase();
            const iconMap = {
                // Audio
                'mp3': 'bi-file-earmark-music',
                'wav': 'bi-file-earmark-music',
                'flac': 'bi-file-earmark-music',
                'aac': 'bi-file-earmark-music',
                'ogg': 'bi-file-earmark-music',
                'm4a': 'bi-file-earmark-music',
                // Video
                'mp4': 'bi-file-earmark-play',
                'avi': 'bi-file-earmark-play',
                'mkv': 'bi-file-earmark-play',
                'mov': 'bi-file-earmark-play',
                'wmv': 'bi-file-earmark-play',
                'webm': 'bi-file-earmark-play',
                // Images
                'jpg': 'bi-file-earmark-image',
                'jpeg': 'bi-file-earmark-image',
                'png': 'bi-file-earmark-image',
                'gif': 'bi-file-earmark-image',
                'bmp': 'bi-file-earmark-image',
                'webp': 'bi-file-earmark-image',
                'svg': 'bi-file-earmark-image',
                // Documents
                'pdf': 'bi-file-earmark-pdf',
                'doc': 'bi-file-earmark-word',
                'docx': 'bi-file-earmark-word',
                'xls': 'bi-file-earmark-excel',
                'xlsx': 'bi-file-earmark-excel',
                'ppt': 'bi-file-earmark-ppt',
                'pptx': 'bi-file-earmark-ppt',
                // Archives
                'zip': 'bi-file-earmark-zip',
                'rar': 'bi-file-earmark-zip',
                '7z': 'bi-file-earmark-zip',
                'tar': 'bi-file-earmark-zip',
                'gz': 'bi-file-earmark-zip',
                // Text
                'txt': 'bi-file-earmark-text',
                'md': 'bi-file-earmark-text',
                'json': 'bi-file-earmark-code',
                'xml': 'bi-file-earmark-code',
                'html': 'bi-file-earmark-code',
                'css': 'bi-file-earmark-code',
                'js': 'bi-file-earmark-code',
                // Executables
                'exe': 'bi-file-earmark-binary',
                'msi': 'bi-file-earmark-binary',
                'apk': 'bi-file-earmark-binary'
            };

            return iconMap[ext] || 'bi-file-earmark';
        },
        bytes(data) {
            if (!data || data === 0) return '0 B';

            const const_term = 1024;

            if (data < const_term) {
                return data + ' B';
            }

            let result = (data / const_term).toFixed(2);
            if (result < 1024) {
                return (result + " KB");
            }
            result = (data / const_term ** 2).toFixed(2);

            if (result < 1024) {
                return (result + " MB");
            }
            result = (data / const_term ** 3).toFixed(2);

            if (result < 1024) {
                return (result + " GB");
            }
            return (data / const_term ** 4).toFixed(2) + " TB";
        },
        bytesProgress(data) {
            return this.bytes(data);
        }
    },
    //computed: {
    //    bytes: function (data) {
    //        const const_term = 1024;

    //        let result = (data / const_term).toFixed(3);
    //        if (result < 1024) {
    //            return (result + "KB");
    //        }
    //        result = (data / const_term ** 2).toFixed(3);

    //        if (result < 1024) {
    //            return (result + "MB");
    //        }
    //        result = (data / const_term ** 3).toFixed(3);

    //        return (result + "GB");

    //        if (result < 1024) {
    //            return (result + "GB");
    //        }
    //        return (data / const_term ** 4).toFixed(3) + "TB";

    //    },
    //},
    }).mount('#modalFileUploadVue');

    // Guardar referencia al elemento montado
    mountedElement = mountElement;
    console.log('[FileUpload] Vue montado correctamente');
}

// Inicializar cuando el DOM esté listo
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initFileUploadVue);
} else {
    // DOM ya está cargado, inicializar después de un pequeño delay para asegurar que Blazor haya renderizado
    setTimeout(initFileUploadVue, 100);
}

// Función para reiniciar Vue
function resetAndInitVue() {
    console.log('[FileUpload] Reiniciando Vue...');
    if (fileModal) {
        try {
            fileModal.unmount();
        } catch (e) {
            console.log('[FileUpload] Error al desmontar:', e);
        }
    }
    fileModal = null;
    mountedElement = null;
    initAttempts = 0;
    setTimeout(initFileUploadVue, 150);
}

// Usar MutationObserver para detectar cuando el elemento se recrea
let observer = null;
function setupMutationObserver() {
    if (observer) return;

    observer = new MutationObserver((mutations) => {
        const currentElement = document.getElementById('modalFileUploadVue');

        // Si el elemento existe pero es diferente al que tenemos montado
        if (currentElement && mountedElement && currentElement !== mountedElement) {
            console.log('[FileUpload] Elemento recreado, reinicializando...');
            resetAndInitVue();
        }
        // Si el elemento existe pero no tenemos Vue montado
        else if (currentElement && !fileModal) {
            console.log('[FileUpload] Elemento encontrado sin Vue, inicializando...');
            initFileUploadVue();
        }
    });

    observer.observe(document.body, {
        childList: true,
        subtree: true
    });
}

// Iniciar observer después de un pequeño delay
setTimeout(setupMutationObserver, 500);

// Exponer función de reinicio para Blazor
window.initFileUploadVue = initFileUploadVue;
window.resetFileUploadVue = resetAndInitVue;

let openModalRetries = 0;
const MAX_OPEN_RETRIES = 30; // 3 segundos máximo

window.openFileUploadModal = (id, path, url) => {
    console.log('[FileUpload] openFileUploadModal llamado:', { id, path, url });

    // Asegurar que Vue esté inicializado
    if (!fileModal) {
        if (openModalRetries < MAX_OPEN_RETRIES) {
            openModalRetries++;
            console.log('[FileUpload] Vue no inicializado, reintentando...', openModalRetries);
            initFileUploadVue();
            // Esperar un poco y reintentar
            setTimeout(() => window.openFileUploadModal(id, path, url), 100);
            return;
        } else {
            console.error('[FileUpload] No se pudo abrir el modal después de múltiples intentos');
            openModalRetries = 0;
            return;
        }
    }

    openModalRetries = 0; // Resetear contador
    console.log('[FileUpload] Abriendo modal...');

    fileModal.id = id;
    fileModal.path = path;
    fileModal.url = url;
    fileModal.files = [];
    fileModal.showModal = true;
}

window.openAudioPlayerModal = (file, type = "audio/mpeg", title = "") => {
    // Call Blazor component via JSInvokable
    if (type === null) {
        type = "audio/mpeg";
    }
    try {
        DotNet.invokeMethodAsync('TelegramDownloader', 'OpenAudioPlayer', file, type, title).catch(() => {});
    } catch (e) { console.log('OpenAudioPlayer error:', e); }
}

window.openAudioModal = () => {
    // Abrir el modal con la canción actual (si hay una)
    try {
        DotNet.invokeMethodAsync('TelegramDownloader', 'OpenAudioPlayerCurrent').catch(() => {});
    } catch (e) { console.log('OpenAudioPlayerCurrent error:', e); }
}

window.playAudioPlayer = (url, type) => {
    const audio = document.getElementById('audioPlayer');
    if (audio) {
        // Establecer src directamente
        if (url) {
            audio.src = url;
            if (type) {
                audio.type = type;
            }
        }
        audio.load();
        audio.play().catch(e => console.log('Audio play error:', e));
    }
}

window.isAudioPlaying = () => {
    const audio = document.getElementById('audioPlayer');
    return audio ? !audio.paused : false;
}

window.stopAudioPlayer = () => {
    const audio = document.getElementById('audioPlayer');
    if (audio) {
        audio.pause();
        audio.currentTime = 0;
    }
}

window.pauseAudioPlayer = () => {
    const audio = document.getElementById('audioPlayer');
    if (audio) {
        audio.pause();
    }
}

window.resumeAudioPlayer = () => {
    const audio = document.getElementById('audioPlayer');
    if (audio) {
        audio.play().catch(e => console.log('Audio play error:', e));
    }
}

window.seekAudioPlayer = (percent) => {
    const audio = document.getElementById('audioPlayer');
    if (audio && audio.duration) {
        audio.currentTime = (percent / 100) * audio.duration;
    }
}

window.setAudioVolume = (volume) => {
    const audio = document.getElementById('audioPlayer');
    if (audio) {
        audio.volume = volume;
    }
}

window.setAudioMuted = (muted) => {
    const audio = document.getElementById('audioPlayer');
    if (audio) {
        audio.muted = muted;
    }
}

window.getAudioInfo = () => {
    const audio = document.getElementById('audioPlayer');
    if (!audio) {
        return { isPlaying: false, currentTime: 0, duration: 0, progress: 0, bufferPercent: 0 };
    }

    const duration = audio.duration || 0;
    const currentTime = audio.currentTime || 0;
    const progress = duration > 0 ? (currentTime / duration) * 100 : 0;

    // Calcular buffer
    let bufferPercent = 0;
    if (audio.buffered.length > 0 && duration > 0) {
        const bufferedEnd = audio.buffered.end(audio.buffered.length - 1);
        bufferPercent = (bufferedEnd / duration) * 100;
    }

    return {
        isPlaying: !audio.paused,
        currentTime: currentTime,
        duration: duration,
        progress: progress,
        bufferPercent: bufferPercent
    };
}

window.closeAudioModal = () => {
    // El audio sigue reproduciéndose en segundo plano
}

// ===== Audio Visualizer with Web Audio API =====
window._audioVisualizer = {
    audioContext: null,
    analyser: null,
    source: null,
    dataArray: null,
    animationId: null,
    isInitialized: false
};

window.initAudioVisualizer = () => {
    const audio = document.getElementById('audioPlayer');
    if (!audio) return false;

    try {
        // Only create context once
        if (!window._audioVisualizer.audioContext) {
            window._audioVisualizer.audioContext = new (window.AudioContext || window.webkitAudioContext)();
        }

        const ctx = window._audioVisualizer.audioContext;

        // Resume context if suspended (browser autoplay policy)
        if (ctx.state === 'suspended') {
            ctx.resume();
        }

        // Only connect source once per audio element
        if (!window._audioVisualizer.isInitialized) {
            // Create analyser with better resolution
            window._audioVisualizer.analyser = ctx.createAnalyser();
            window._audioVisualizer.analyser.fftSize = 256; // 128 frequency bins for better resolution
            window._audioVisualizer.analyser.smoothingTimeConstant = 0.4; // Lower = more responsive
            window._audioVisualizer.analyser.minDecibels = -90;
            window._audioVisualizer.analyser.maxDecibels = -10;

            // Connect audio element to analyser
            window._audioVisualizer.source = ctx.createMediaElementSource(audio);
            window._audioVisualizer.source.connect(window._audioVisualizer.analyser);
            window._audioVisualizer.analyser.connect(ctx.destination);

            // Create data array for frequency data
            const bufferLength = window._audioVisualizer.analyser.frequencyBinCount;
            window._audioVisualizer.dataArray = new Uint8Array(bufferLength);

            window._audioVisualizer.isInitialized = true;
        }

        return true;
    } catch (e) {
        console.error('Error initializing audio visualizer:', e);
        return false;
    }
};

window.getVisualizerData = () => {
    if (!window._audioVisualizer.analyser || !window._audioVisualizer.dataArray) {
        return null;
    }

    try {
        window._audioVisualizer.analyser.getByteFrequencyData(window._audioVisualizer.dataArray);

        const data = window._audioVisualizer.dataArray;
        const bars = [];
        const numBars = 13;
        const dataLength = data.length;

        // Use logarithmic frequency distribution (more bins for bass, fewer for treble)
        // This better matches human hearing perception
        const frequencyBands = [
            { start: 0, end: 2 },      // Sub-bass (very low)
            { start: 2, end: 4 },      // Bass
            { start: 4, end: 8 },      // Low-mid bass
            { start: 8, end: 12 },     // Mid-bass
            { start: 12, end: 20 },    // Low-mids
            { start: 20, end: 30 },    // Mids
            { start: 30, end: 42 },    // Upper-mids
            { start: 42, end: 56 },    // Presence
            { start: 56, end: 72 },    // High-mids
            { start: 72, end: 88 },    // Highs
            { start: 88, end: 104 },   // High treble
            { start: 104, end: 118 },  // Air
            { start: 118, end: 128 }   // Ultra-high
        ];

        // Frequency-dependent scaling to compensate for bass dominance
        // Lower values = more attenuation, higher values = more boost
        const frequencyScaling = [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7];

        for (let i = 0; i < numBars; i++) {
            const band = frequencyBands[i];
            let sum = 0;
            let count = 0;

            for (let j = band.start; j < band.end && j < dataLength; j++) {
                sum += data[j];
                count++;
            }

            if (count > 0) {
                let avg = sum / count;
                // Apply frequency-dependent scaling
                avg *= frequencyScaling[i];
                // Normalize to 0-100
                let normalized = (avg / 255) * 100;
                // Apply slight boost for visual impact
                normalized = Math.min(100, normalized * 1.2);
                bars.push(normalized);
            } else {
                bars.push(0);
            }
        }

        return bars;
    } catch (e) {
        console.error('Error getting visualizer data:', e);
        return null;
    }
};

window.startVisualizerAnimation = (callback) => {
    // Initialize if not already done
    if (!window._audioVisualizer.isInitialized) {
        window.initAudioVisualizer();
    }

    const animate = () => {
        const data = window.getVisualizerData();
        if (data && callback) {
            callback(data);
        }
        window._audioVisualizer.animationId = requestAnimationFrame(animate);
    };

    animate();
};

window.stopVisualizerAnimation = () => {
    if (window._audioVisualizer.animationId) {
        cancelAnimationFrame(window._audioVisualizer.animationId);
        window._audioVisualizer.animationId = null;
    }
};

// Destroy the audio visualizer and disconnect Web Audio API
// This can help with audio quality for FLAC and other high-quality formats
window.destroyAudioVisualizer = () => {
    try {
        // Stop any running animation
        window.stopVisualizerAnimation();

        // Disconnect the analyser from the audio chain if connected
        if (window._audioVisualizer.analyser) {
            try {
                window._audioVisualizer.analyser.disconnect();
            } catch (e) { /* already disconnected */ }
        }

        // Disconnect the source but keep it (can't reconnect MediaElementSource)
        // The source will remain connected to destination for audio playback
        if (window._audioVisualizer.source && window._audioVisualizer.audioContext) {
            try {
                // Reconnect source directly to destination (bypassing analyser)
                window._audioVisualizer.source.disconnect();
                window._audioVisualizer.source.connect(window._audioVisualizer.audioContext.destination);
            } catch (e) { /* ignore */ }
        }

        // Clear the data array
        window._audioVisualizer.dataArray = null;
        window._audioVisualizer.analyser = null;

        // Mark as not initialized so it can be re-initialized if needed
        window._audioVisualizer.isInitialized = false;

        console.log('Audio visualizer destroyed - audio quality mode enabled');
        return true;
    } catch (e) {
        console.error('Error destroying audio visualizer:', e);
        return false;
    }
};

window.addToAudioPlaylist = (url, type = "audio/mpeg", title = "") => {
    if (type === null) type = "audio/mpeg";
    try {
        DotNet.invokeMethodAsync('TelegramDownloader', 'AddToAudioPlaylist', url, type, title).catch(() => {});
    } catch (e) { console.log('AddToAudioPlaylist error:', e); }
}

window.addToAudioPlaylistAndPlay = (url, type = "audio/mpeg", title = "") => {
    if (type === null) type = "audio/mpeg";
    try {
        DotNet.invokeMethodAsync('TelegramDownloader', 'AddToAudioPlaylistAndPlay', url, type, title).catch(() => {});
    } catch (e) { console.log('AddToAudioPlaylistAndPlay error:', e); }
}

window.addMultipleToAudioPlaylist = (items) => {
    // items is an array of {url, type, title} objects
    try {
        DotNet.invokeMethodAsync('TelegramDownloader', 'AddMultipleToAudioPlaylist', items).catch(() => {});
    } catch (e) { console.log('AddMultipleToAudioPlaylist error:', e); }
}

// ===== Media Session API for mobile/Bluetooth integration =====

window.initMediaSession = (title, artist, album, artworkUrl) => {
    if (!('mediaSession' in navigator)) {
        console.log('Media Session API not supported');
        return false;
    }

    try {
        // Set metadata
        navigator.mediaSession.metadata = new MediaMetadata({
            title: title || 'Unknown Track',
            artist: artist || 'TelegramFileManager',
            album: album || 'Playlist',
            artwork: artworkUrl ? [
                { src: artworkUrl, sizes: '96x96', type: 'image/png' },
                { src: artworkUrl, sizes: '128x128', type: 'image/png' },
                { src: artworkUrl, sizes: '192x192', type: 'image/png' },
                { src: artworkUrl, sizes: '256x256', type: 'image/png' },
                { src: artworkUrl, sizes: '384x384', type: 'image/png' },
                { src: artworkUrl, sizes: '512x512', type: 'image/png' }
            ] : []
        });

        // Set up action handlers with error handling
        navigator.mediaSession.setActionHandler('play', () => {
            try {
                DotNet.invokeMethodAsync('TelegramDownloader', 'MediaSessionAction', 'play').catch(() => {});
            } catch (e) { console.log('Media session play error:', e); }
        });

        navigator.mediaSession.setActionHandler('pause', () => {
            try {
                DotNet.invokeMethodAsync('TelegramDownloader', 'MediaSessionAction', 'pause').catch(() => {});
            } catch (e) { console.log('Media session pause error:', e); }
        });

        navigator.mediaSession.setActionHandler('previoustrack', () => {
            try {
                DotNet.invokeMethodAsync('TelegramDownloader', 'MediaSessionAction', 'previoustrack').catch(() => {});
            } catch (e) { console.log('Media session previoustrack error:', e); }
        });

        navigator.mediaSession.setActionHandler('nexttrack', () => {
            try {
                DotNet.invokeMethodAsync('TelegramDownloader', 'MediaSessionAction', 'nexttrack').catch(() => {});
            } catch (e) { console.log('Media session nexttrack error:', e); }
        });

        navigator.mediaSession.setActionHandler('stop', () => {
            try {
                DotNet.invokeMethodAsync('TelegramDownloader', 'MediaSessionAction', 'stop').catch(() => {});
            } catch (e) { console.log('Media session stop error:', e); }
        });

        // Seek handlers (for progress bar on lock screen)
        try {
            navigator.mediaSession.setActionHandler('seekbackward', (details) => {
                try {
                    const skipTime = details.seekOffset || 10;
                    DotNet.invokeMethodAsync('TelegramDownloader', 'MediaSessionSeek', -skipTime).catch(() => {});
                } catch (e) { console.log('Media session seekbackward error:', e); }
            });

            navigator.mediaSession.setActionHandler('seekforward', (details) => {
                try {
                    const skipTime = details.seekOffset || 10;
                    DotNet.invokeMethodAsync('TelegramDownloader', 'MediaSessionSeek', skipTime).catch(() => {});
                } catch (e) { console.log('Media session seekforward error:', e); }
            });

            navigator.mediaSession.setActionHandler('seekto', (details) => {
                try {
                    if (details.seekTime !== undefined) {
                        DotNet.invokeMethodAsync('TelegramDownloader', 'MediaSessionSeekTo', details.seekTime).catch(() => {});
                    }
                } catch (e) { console.log('Media session seekto error:', e); }
            });
        } catch (e) {
            console.log('Seek handlers not supported:', e);
        }

        return true;
    } catch (e) {
        console.error('Error initializing Media Session:', e);
        return false;
    }
}

window.updateMediaSessionMetadata = (title, artist, album, artworkUrl) => {
    if (!('mediaSession' in navigator)) return;

    try {
        navigator.mediaSession.metadata = new MediaMetadata({
            title: title || 'Unknown Track',
            artist: artist || 'TelegramFileManager',
            album: album || 'Playlist',
            artwork: artworkUrl ? [
                { src: artworkUrl, sizes: '96x96', type: 'image/png' },
                { src: artworkUrl, sizes: '128x128', type: 'image/png' },
                { src: artworkUrl, sizes: '192x192', type: 'image/png' },
                { src: artworkUrl, sizes: '256x256', type: 'image/png' },
                { src: artworkUrl, sizes: '384x384', type: 'image/png' },
                { src: artworkUrl, sizes: '512x512', type: 'image/png' }
            ] : []
        });
    } catch (e) {
        console.error('Error updating Media Session metadata:', e);
    }
}

window.updateMediaSessionPlaybackState = (state) => {
    if (!('mediaSession' in navigator)) return;

    try {
        // state: 'playing', 'paused', 'none'
        navigator.mediaSession.playbackState = state;
    } catch (e) {
        console.error('Error updating playback state:', e);
    }
}

window.updateMediaSessionPositionState = (duration, position, playbackRate) => {
    if (!('mediaSession' in navigator)) return;

    try {
        if (duration > 0) {
            navigator.mediaSession.setPositionState({
                duration: duration,
                playbackRate: playbackRate || 1,
                position: Math.min(position, duration)
            });
        }
    } catch (e) {
        console.error('Error updating position state:', e);
    }
}

window.clearMediaSession = () => {
    if (!('mediaSession' in navigator)) return;

    try {
        navigator.mediaSession.metadata = null;
        navigator.mediaSession.playbackState = 'none';
    } catch (e) {
        console.error('Error clearing Media Session:', e);
    }
}

// ===== Audio Artwork Extraction =====

// Cache for extracted artwork to avoid re-fetching
window._artworkCache = {};

window.extractAudioArtwork = (audioUrl) => {
    return new Promise((resolve, reject) => {
        // Check cache first
        if (window._artworkCache[audioUrl]) {
            resolve(window._artworkCache[audioUrl]);
            return;
        }

        // Skip artwork extraction for FLAC files - jsmediatags has issues with them
        // and they require downloading too much data
        const urlLower = audioUrl.toLowerCase();
        if (urlLower.includes('.flac') || urlLower.includes('flac')) {
            console.log('Skipping artwork extraction for FLAC file');
            window._artworkCache[audioUrl] = null;
            resolve(null);
            return;
        }

        // Check if jsmediatags is available
        if (typeof jsmediatags === 'undefined') {
            console.log('jsmediatags library not loaded');
            resolve(null);
            return;
        }

        // Add timeout to prevent hanging
        const timeoutId = setTimeout(() => {
            console.log('Artwork extraction timed out');
            window._artworkCache[audioUrl] = null;
            resolve(null);
        }, 10000); // 10 second timeout

        try {
            jsmediatags.read(audioUrl, {
                onSuccess: function(tag) {
                    clearTimeout(timeoutId);
                    try {
                        const picture = tag.tags.picture;
                        if (picture) {
                            // Convert array buffer to base64
                            const base64String = picture.data.reduce((data, byte) => {
                                return data + String.fromCharCode(byte);
                            }, '');
                            const base64 = btoa(base64String);
                            const mimeType = picture.format || 'image/jpeg';
                            const dataUrl = `data:${mimeType};base64,${base64}`;

                            // Cache the result
                            window._artworkCache[audioUrl] = dataUrl;
                            resolve(dataUrl);
                        } else {
                            window._artworkCache[audioUrl] = null;
                            resolve(null);
                        }
                    } catch (e) {
                        console.error('Error processing artwork:', e);
                        resolve(null);
                    }
                },
                onError: function(error) {
                    clearTimeout(timeoutId);
                    console.log('Error reading tags:', error.type, error.info);
                    window._artworkCache[audioUrl] = null;
                    resolve(null);
                }
            });
        } catch (e) {
            clearTimeout(timeoutId);
            console.error('Error extracting artwork:', e);
            resolve(null);
        }
    });
}

// Extract artwork and notify Blazor
window.extractAndNotifyArtwork = async (audioUrl) => {
    try {
        const artworkUrl = await window.extractAudioArtwork(audioUrl);
        return artworkUrl;
    } catch (e) {
        console.error('Error in extractAndNotifyArtwork:', e);
        return null;
    }
}

// Clear artwork cache for a specific URL or all
window.clearArtworkCache = (audioUrl) => {
    if (audioUrl) {
        delete window._artworkCache[audioUrl];
    } else {
        window._artworkCache = {};
    }
}

window.stopVideoPlayer = () => {
    const video = document.getElementById('videoPlayer');
    if (video) {
        video.pause();
        video.currentTime = 0;
    }
}

window.toggleVideoFullscreen = () => {
    const video = document.getElementById('videoPlayer');
    if (video) {
        if (document.fullscreenElement) {
            document.exitFullscreen();
        } else {
            video.requestFullscreen().catch(err => {
                console.log('Error attempting fullscreen:', err);
            });
        }
    }
}

window.blurActiveElement = () => {
    if (document.activeElement && document.activeElement.blur) {
        document.activeElement.blur();
    }
}

// Move modal to body to avoid stacking context issues
window.moveToBody = (elementSelector) => {
    const element = document.querySelector(elementSelector);
    if (element && element.parentElement !== document.body) {
        document.body.appendChild(element);
    }
}

window.showUploadModal = () => {
    const modal = document.querySelector('.vue-upload-overlay');
    if (modal) {
        // Move to body if not already there
        if (modal.parentElement !== document.body) {
            document.body.appendChild(modal);
        }
    }
}

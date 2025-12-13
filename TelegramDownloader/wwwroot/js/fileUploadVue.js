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
            isUploading: false
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
            console.log("Enviando....");

            const uploadPromises = this.files.map((value) => {
                return new Promise((resolve) => {
                    let xhr = new XMLHttpRequest();
                    xhr.open("POST", this.url);
                    let data = new FormData();
                    data.set('file', value.file);
                    data.set('id', this.id);
                    data.set('path', this.path);
                    data.set('action', 'save');

                    xhr.upload.addEventListener("progress", ({ loaded, total }) => {
                        value.progress = Math.floor((loaded / total) * 100);
                        value.sended = loaded;

                        if (loaded == total) {
                            value.completed = true;
                        }
                    });

                    xhr.onloadend = () => resolve();
                    xhr.send(data);
                });
            });

            await Promise.all(uploadPromises);
            this.isUploading = false;
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
                    DotNet.invokeMethodAsync('TelegramDownloader', 'RefreshFileManagerStatic');
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
    DotNet.invokeMethodAsync('TelegramDownloader', 'OpenAudioPlayer', file, type, title);
}

window.openAudioModal = () => {
    // Abrir el modal con la canción actual (si hay una)
    DotNet.invokeMethodAsync('TelegramDownloader', 'OpenAudioPlayerCurrent');
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

window.addToAudioPlaylist = (url, type = "audio/mpeg", title = "") => {
    if (type === null) type = "audio/mpeg";
    DotNet.invokeMethodAsync('TelegramDownloader', 'AddToAudioPlaylist', url, type, title);
}

window.addToAudioPlaylistAndPlay = (url, type = "audio/mpeg", title = "") => {
    if (type === null) type = "audio/mpeg";
    DotNet.invokeMethodAsync('TelegramDownloader', 'AddToAudioPlaylistAndPlay', url, type, title);
}

window.addMultipleToAudioPlaylist = (items) => {
    // items is an array of {url, type, title} objects
    DotNet.invokeMethodAsync('TelegramDownloader', 'AddMultipleToAudioPlaylist', items);
}

window.stopVideoPlayer = () => {
    const video = document.getElementById('videoPlayer');
    if (video) {
        video.pause();
        video.currentTime = 0;
    }
}

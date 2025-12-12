const { createApp, ref } = Vue

//console.log("Modal es:", fileModal);

const fileModal = createApp({
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
            url: ""
        };
    },
    methods: {
        onChangeFileUpload() {
            let filterFiles = [];
            filterFiles.push(...this.$refs.files.files);
            let listFiles = [];
            filterFiles.forEach(file => {
                const newFile = {
                    file: file,
                    progress: 0,
                    sended: 0,
                    total: this.bytes(file.size),
                    completed: false
                };
                listFiles.push(newFile);
            });
            this.files = listFiles;
            //this.files.push(...(filterFiles.map((file) => {
            //    return {
            //        file: file,
            //        progress: 0,
            //        sended: 0,
            //        total: file.size

            //    }
                

            //})));
        },
        deleteFile(name) {
            this.files = this.files.filter(file => file.file.name !== name);
        },
        async submitForm() {
            console.log("Enviando....");
            this.files.forEach((value, index) => {
                let xhr = new XMLHttpRequest();
                xhr.open("POST", this.url);
                let data = new FormData();
                data.set('file', value.file);
                //data.append('file', value);
                data.set('id', this.id);
                data.set('path', this.path);
                data.set('action', 'save');
                // xhr.setRequestHeader("Content-Type", "multipart/form-data");
                xhr.upload.addEventListener("progress", ({ loaded, total }) => {
                    value.progress = Math.floor((loaded / total) * 100);
                    value.sended = loaded;
                    
                    if (loaded == total) {
                        value.completed = true;
                    }
                    // await this.$nextTick();
                });
                xhr.send(data);
            });
            
        },
        closeModal() {
            this.files = [];
            this.showModal = false;
        },
        closeModalPlayer() {
            this.showAudioModal = false;
        },
        bytes(data) {
            const const_term = 1024;

            let result = (data / const_term).toFixed(2);
            if (result < 1024) {
                return (result + "KB");
            }
            result = (data / const_term ** 2).toFixed(2);

            if (result < 1024) {
                return (result + "MB");
            }
            result = (data / const_term ** 3).toFixed(2);

            return (result + "GB");

            if (result < 1024) {
                return (result + "GB");
            }
            return (data / const_term ** 4).toFixed(2) + "TB";
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

window.openFileUploadModal = (id, path, url) => {
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

window.stopVideoPlayer = () => {
    const video = document.getElementById('videoPlayer');
    if (video) {
        video.pause();
        video.currentTime = 0;
    }
}

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

window.openAudioPlayerModal = (file, type = "audio/mpeg") => {
    // https://stackoverflow.com/questions/10792163/change-audio-src-with-javascript
    fileModal.showAudioModal = true;
    var source = document.getElementById('audioSource');
    source.src = file;
    source.type = type;
    document.getElementById("audiomusic").load();
    document.getElementById("audiomusic").play();
    
}

window.openAudioModal = () => {
    // https://stackoverflow.com/questions/10792163/change-audio-src-with-javascript
    fileModal.showAudioModal = true;


}

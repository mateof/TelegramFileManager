
////Dropzone.autoDiscover = false;
//Dropzone.discover();
////window.onload = function () {
////    Dropzone.discover();
////};
////Dropzone.options.myDropzone = false;

//    Dropzone.options.myZone = { // camelized version of the `id`
//        autoProcessQueue: false,
//        uploadMultiple: true,
//        parallelUploads: 100,
//        maxFiles: 100,
//        paramName: "uploadFiles", // The name that will be used to transfer the file
//        maxFilesize: 999999, // MB
//        accept: function (file, done) {
//            if (file.name == "justinbieber.jpg") {
//                done("Naha, you don't.");
//            }
//            else { done(); }
//        },
//        dictDefaultMessage: "Upload your file here",
//        init: function () {
//            this.on("sending", function (file, xhr, formData) {
//                console.log("sending file");
//            });
//            this.on("success", function (file, responseText) {
//                console.log('great success');
//            });
//            this.on("addedfile", function (file) {
//                console.log('file added');
//            });
//        }

//    };
//    var zone = Dropzone.options.myZone;
//    console.log(zone);


Dropzone.discover();
Dropzone.options.myZone = { // camelized version of the `id`
    autoProcessQueue: true,
    uploadMultiple: false,
    url: document.getElementById("myZone").getAttribute('data-url'),
    // parallelUploads: 100,
    maxFiles: 100,
    paramName: "file", // The name that will be used to transfer the file
    maxFilesize: 999999, // MB
    accept: function (file, done) {
        if (file.name == "justinbieber.jpg") {
            done("Naha, you don't.");
        }
        else { done(); }
    },
    dictDefaultMessage: "Upload your file here",
    // init: function () {
    //     this.on("sending", function (file, xhr, formData) {
    //         console.log("sending file");
    //     });
    //     this.on("success", function (file, responseText) {
    //         console.log('great success');
    //     });
    //     this.on("addedfile", function (file) {
    //         console.log('file added');
    //     });
    // }

};
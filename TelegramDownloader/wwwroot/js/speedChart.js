let speedChart = null;
let chartMaxPoints = 200;
let downloadData = [];
let uploadData = [];
let downloadMeta = [];
let uploadMeta = [];

function initSpeedChart(maxPoints, intervalSeconds) {
    const ctx = document.getElementById('speedChart');
    if (!ctx) return;

    if (speedChart) {
        speedChart.destroy();
    }

    chartMaxPoints = maxPoints || 200;

    // Initialize with empty data (nulls) - oldest on left, newest on right
    downloadData = new Array(chartMaxPoints).fill(null);
    uploadData = new Array(chartMaxPoints).fill(null);
    downloadMeta = new Array(chartMaxPoints).fill(null);
    uploadMeta = new Array(chartMaxPoints).fill(null);
    const labels = new Array(chartMaxPoints).fill('');

    speedChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [
                {
                    label: 'Download (MB/s)',
                    data: [...downloadData],
                    borderColor: 'rgb(75, 192, 192)',
                    backgroundColor: 'rgba(75, 192, 192, 0.1)',
                    tension: 0.3,
                    fill: true
                },
                {
                    label: 'Upload (MB/s)',
                    data: [...uploadData],
                    borderColor: 'rgb(255, 99, 132)',
                    backgroundColor: 'rgba(255, 99, 132, 0.1)',
                    tension: 0.3,
                    fill: true
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'index',
                intersect: false,
            },
            plugins: {
                legend: {
                    position: 'top',
                },
                title: {
                    display: true,
                    text: 'Speed History'
                },
                tooltip: {
                    callbacks: {
                        title: function(context) {
                            const index = context[0].dataIndex;
                            const meta = downloadMeta[index] || uploadMeta[index];
                            if (meta && meta.datetime) {
                                return meta.datetime;
                            }
                            return '';
                        },
                        afterBody: function(context) {
                            const index = context[0].dataIndex;
                            const lines = [];

                            // Download files
                            const dlMeta = downloadMeta[index];
                            if (dlMeta && dlMeta.files && dlMeta.files.length > 0) {
                                lines.push('');
                                lines.push('Downloading:');
                                dlMeta.files.forEach(f => lines.push('  • ' + f));
                            }

                            // Upload files
                            const ulMeta = uploadMeta[index];
                            if (ulMeta && ulMeta.files && ulMeta.files.length > 0) {
                                lines.push('');
                                lines.push('Uploading:');
                                ulMeta.files.forEach(f => lines.push('  • ' + f));
                            }

                            return lines;
                        }
                    }
                }
            },
            scales: {
                x: {
                    display: false
                },
                y: {
                    display: true,
                    title: {
                        display: true,
                        text: 'Speed (MB/s)'
                    },
                    beginAtZero: true
                }
            }
        }
    });
}

function updateSpeedChart(downloadHistory, uploadHistory, isInitial) {
    if (!speedChart) return;

    if (isInitial && downloadHistory.length > 0) {
        // Load all historical data on initial load
        // Fill from the right side (most recent on right)
        const historyLength = downloadHistory.length;
        const startIndex = chartMaxPoints - historyLength;

        // Reset arrays
        downloadData = new Array(chartMaxPoints).fill(null);
        uploadData = new Array(chartMaxPoints).fill(null);
        downloadMeta = new Array(chartMaxPoints).fill(null);
        uploadMeta = new Array(chartMaxPoints).fill(null);

        for (let i = 0; i < historyLength; i++) {
            const targetIndex = startIndex + i;
            if (targetIndex >= 0 && targetIndex < chartMaxPoints) {
                downloadData[targetIndex] = downloadHistory[i].speed;
                uploadData[targetIndex] = uploadHistory[i] ? uploadHistory[i].speed : null;
                downloadMeta[targetIndex] = {
                    datetime: downloadHistory[i].datetime,
                    files: downloadHistory[i].files
                };
                uploadMeta[targetIndex] = uploadHistory[i] ? {
                    datetime: uploadHistory[i].datetime,
                    files: uploadHistory[i].files
                } : null;
            }
        }
    } else if (downloadHistory.length > 0) {
        // Get the latest values (last element in the arrays)
        const latestDownload = downloadHistory[downloadHistory.length - 1];
        const latestUpload = uploadHistory.length > 0 ? uploadHistory[uploadHistory.length - 1] : null;

        // Shift data left (remove oldest) and add new value on the right
        downloadData.shift();
        downloadData.push(latestDownload ? latestDownload.speed : null);

        uploadData.shift();
        uploadData.push(latestUpload ? latestUpload.speed : null);

        downloadMeta.shift();
        downloadMeta.push(latestDownload ? {
            datetime: latestDownload.datetime,
            files: latestDownload.files
        } : null);

        uploadMeta.shift();
        uploadMeta.push(latestUpload ? {
            datetime: latestUpload.datetime,
            files: latestUpload.files
        } : null);
    }

    speedChart.data.datasets[0].data = [...downloadData];
    speedChart.data.datasets[1].data = [...uploadData];
    speedChart.update('none');
}

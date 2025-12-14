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

    // Create gradient for download
    const downloadGradient = ctx.getContext('2d').createLinearGradient(0, 0, 0, 300);
    downloadGradient.addColorStop(0, 'rgba(34, 197, 94, 0.4)');
    downloadGradient.addColorStop(0.5, 'rgba(34, 197, 94, 0.15)');
    downloadGradient.addColorStop(1, 'rgba(34, 197, 94, 0)');

    // Create gradient for upload
    const uploadGradient = ctx.getContext('2d').createLinearGradient(0, 0, 0, 300);
    uploadGradient.addColorStop(0, 'rgba(233, 69, 96, 0.4)');
    uploadGradient.addColorStop(0.5, 'rgba(233, 69, 96, 0.15)');
    uploadGradient.addColorStop(1, 'rgba(233, 69, 96, 0)');

    speedChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [
                {
                    label: 'Download',
                    data: [...downloadData],
                    borderColor: 'rgb(34, 197, 94)',
                    backgroundColor: downloadGradient,
                    borderWidth: 2.5,
                    tension: 0.4,
                    fill: true,
                    pointRadius: 0,
                    pointHoverRadius: 6,
                    pointHoverBackgroundColor: 'rgb(34, 197, 94)',
                    pointHoverBorderColor: '#fff',
                    pointHoverBorderWidth: 2
                },
                {
                    label: 'Upload',
                    data: [...uploadData],
                    borderColor: 'rgb(233, 69, 96)',
                    backgroundColor: uploadGradient,
                    borderWidth: 2.5,
                    tension: 0.4,
                    fill: true,
                    pointRadius: 0,
                    pointHoverRadius: 6,
                    pointHoverBackgroundColor: 'rgb(233, 69, 96)',
                    pointHoverBorderColor: '#fff',
                    pointHoverBorderWidth: 2
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
                    display: false // We'll use custom legend
                },
                title: {
                    display: false
                },
                tooltip: {
                    enabled: true,
                    backgroundColor: 'rgba(26, 26, 46, 0.95)',
                    titleColor: '#fff',
                    bodyColor: 'rgba(255, 255, 255, 0.8)',
                    borderColor: 'rgba(255, 255, 255, 0.1)',
                    borderWidth: 1,
                    cornerRadius: 8,
                    padding: 12,
                    titleFont: {
                        size: 13,
                        weight: '600'
                    },
                    bodyFont: {
                        size: 12
                    },
                    displayColors: true,
                    boxWidth: 12,
                    boxHeight: 12,
                    boxPadding: 4,
                    usePointStyle: true,
                    callbacks: {
                        title: function(context) {
                            const index = context[0].dataIndex;
                            const meta = downloadMeta[index] || uploadMeta[index];
                            if (meta && meta.datetime) {
                                return meta.datetime;
                            }
                            return '';
                        },
                        label: function(context) {
                            const value = context.parsed.y;
                            if (value === null || value === undefined) return null;
                            const label = context.dataset.label;
                            return ` ${label}: ${value.toFixed(2)} MB/s`;
                        },
                        afterBody: function(context) {
                            const index = context[0].dataIndex;
                            const lines = [];

                            // Download files
                            const dlMeta = downloadMeta[index];
                            if (dlMeta && dlMeta.files && dlMeta.files.length > 0) {
                                lines.push('');
                                lines.push('Downloading:');
                                dlMeta.files.slice(0, 3).forEach(f => lines.push('  ' + truncateFileName(f, 30)));
                                if (dlMeta.files.length > 3) {
                                    lines.push('  +' + (dlMeta.files.length - 3) + ' more...');
                                }
                            }

                            // Upload files
                            const ulMeta = uploadMeta[index];
                            if (ulMeta && ulMeta.files && ulMeta.files.length > 0) {
                                lines.push('');
                                lines.push('Uploading:');
                                ulMeta.files.slice(0, 3).forEach(f => lines.push('  ' + truncateFileName(f, 30)));
                                if (ulMeta.files.length > 3) {
                                    lines.push('  +' + (ulMeta.files.length - 3) + ' more...');
                                }
                            }

                            return lines;
                        }
                    }
                }
            },
            scales: {
                x: {
                    display: false,
                    grid: {
                        display: false
                    }
                },
                y: {
                    display: true,
                    position: 'right',
                    grid: {
                        color: 'rgba(0, 0, 0, 0.06)',
                        drawBorder: false
                    },
                    border: {
                        display: false
                    },
                    ticks: {
                        color: '#9ca3af',
                        font: {
                            size: 11
                        },
                        padding: 8,
                        callback: function(value) {
                            return value.toFixed(1) + ' MB/s';
                        }
                    },
                    beginAtZero: true
                }
            },
            elements: {
                line: {
                    capBezierPoints: true
                }
            }
        }
    });
}

function truncateFileName(name, maxLength) {
    if (!name || name.length <= maxLength) return name;
    const ext = name.lastIndexOf('.') > 0 ? name.substring(name.lastIndexOf('.')) : '';
    const nameWithoutExt = name.substring(0, name.length - ext.length);
    const truncatedName = nameWithoutExt.substring(0, maxLength - ext.length - 3) + '...';
    return truncatedName + ext;
}

// Load initial historical data (called once on component init)
function loadSpeedChartHistory(downloadHistory, uploadHistory) {
    if (!speedChart) return;

    if (downloadHistory.length > 0) {
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

        speedChart.data.datasets[0].data = [...downloadData];
        speedChart.data.datasets[1].data = [...uploadData];
        speedChart.update('none');
    }

    // Update stats after loading history
    updateChartStats();
}

// Add a single new point (called for each new speed event)
function addSpeedChartPoint(downloadPoint, uploadPoint) {
    if (!speedChart) return;

    // Shift data left (remove oldest) and add new value on the right
    downloadData.shift();
    downloadData.push(downloadPoint ? downloadPoint.speed : null);

    uploadData.shift();
    uploadData.push(uploadPoint ? uploadPoint.speed : null);

    downloadMeta.shift();
    downloadMeta.push(downloadPoint ? {
        datetime: downloadPoint.datetime,
        files: downloadPoint.files
    } : null);

    uploadMeta.shift();
    uploadMeta.push(uploadPoint ? {
        datetime: uploadPoint.datetime,
        files: uploadPoint.files
    } : null);

    speedChart.data.datasets[0].data = [...downloadData];
    speedChart.data.datasets[1].data = [...uploadData];
    speedChart.update('none');

    // Update stats
    updateChartStats();
}

function updateChartStats() {
    // Calculate stats for download
    const dlValues = downloadData.filter(v => v !== null && v > 0);
    const dlCurrent = dlValues.length > 0 ? dlValues[dlValues.length - 1] : 0;
    const dlMax = dlValues.length > 0 ? Math.max(...dlValues) : 0;
    const dlAvg = dlValues.length > 0 ? dlValues.reduce((a, b) => a + b, 0) / dlValues.length : 0;

    // Calculate stats for upload
    const ulValues = uploadData.filter(v => v !== null && v > 0);
    const ulCurrent = ulValues.length > 0 ? ulValues[ulValues.length - 1] : 0;
    const ulMax = ulValues.length > 0 ? Math.max(...ulValues) : 0;
    const ulAvg = ulValues.length > 0 ? ulValues.reduce((a, b) => a + b, 0) / ulValues.length : 0;

    // Update DOM elements if they exist
    const dlCurrentEl = document.getElementById('dl-current');
    const dlMaxEl = document.getElementById('dl-max');
    const dlAvgEl = document.getElementById('dl-avg');
    const ulCurrentEl = document.getElementById('ul-current');
    const ulMaxEl = document.getElementById('ul-max');
    const ulAvgEl = document.getElementById('ul-avg');

    if (dlCurrentEl) dlCurrentEl.textContent = dlCurrent.toFixed(2);
    if (dlMaxEl) dlMaxEl.textContent = dlMax.toFixed(2);
    if (dlAvgEl) dlAvgEl.textContent = dlAvg.toFixed(2);
    if (ulCurrentEl) ulCurrentEl.textContent = ulCurrent.toFixed(2);
    if (ulMaxEl) ulMaxEl.textContent = ulMax.toFixed(2);
    if (ulAvgEl) ulAvgEl.textContent = ulAvg.toFixed(2);
}

// Legacy function - kept for backwards compatibility
function updateSpeedChart(downloadHistory, uploadHistory, isInitial) {
    if (!speedChart) return;

    if (isInitial && downloadHistory.length > 0) {
        loadSpeedChartHistory(downloadHistory, uploadHistory);
    } else if (downloadHistory.length > 0) {
        // Get the latest values (last element in the arrays)
        const latestDownload = downloadHistory[downloadHistory.length - 1];
        const latestUpload = uploadHistory.length > 0 ? uploadHistory[uploadHistory.length - 1] : null;
        addSpeedChartPoint(latestDownload, latestUpload);
    }
}

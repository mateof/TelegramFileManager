var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'))
var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
    return new bootstrap.Tooltip(tooltipTriggerEl)
})

function copyToClipboard(text) {
    navigator.clipboard.writeText(text);
}

function focusElement (id) {
    const element = document.getElementById(id);
    element.focus();
}

// Confetti celebration effect
function triggerConfetti() {
    const duration = 3000;
    const colors = ['#E94560', '#0F3460', '#16213E', '#FFD700', '#00D4FF', '#FF6B6B', '#4ECDC4'];

    // Create canvas
    const canvas = document.createElement('canvas');
    canvas.id = 'confetti-canvas';
    canvas.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:9999;';
    document.body.appendChild(canvas);

    const ctx = canvas.getContext('2d');
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;

    const particles = [];
    const particleCount = 150;

    // Create particles
    for (let i = 0; i < particleCount; i++) {
        particles.push({
            x: Math.random() * canvas.width,
            y: Math.random() * canvas.height - canvas.height,
            size: Math.random() * 10 + 5,
            color: colors[Math.floor(Math.random() * colors.length)],
            speedY: Math.random() * 3 + 2,
            speedX: Math.random() * 4 - 2,
            rotation: Math.random() * 360,
            rotationSpeed: Math.random() * 10 - 5,
            shape: Math.random() > 0.5 ? 'rect' : 'circle',
            opacity: 1
        });
    }

    const startTime = Date.now();

    function animate() {
        const elapsed = Date.now() - startTime;

        if (elapsed > duration) {
            canvas.remove();
            return;
        }

        ctx.clearRect(0, 0, canvas.width, canvas.height);

        // Fade out in the last 500ms
        const fadeStart = duration - 500;
        const globalOpacity = elapsed > fadeStart ? 1 - (elapsed - fadeStart) / 500 : 1;

        particles.forEach(p => {
            ctx.save();
            ctx.globalAlpha = globalOpacity * p.opacity;
            ctx.translate(p.x, p.y);
            ctx.rotate(p.rotation * Math.PI / 180);
            ctx.fillStyle = p.color;

            if (p.shape === 'rect') {
                ctx.fillRect(-p.size / 2, -p.size / 2, p.size, p.size * 0.6);
            } else {
                ctx.beginPath();
                ctx.arc(0, 0, p.size / 2, 0, Math.PI * 2);
                ctx.fill();
            }

            ctx.restore();

            // Update position
            p.y += p.speedY;
            p.x += p.speedX;
            p.rotation += p.rotationSpeed;
            p.speedY += 0.1; // gravity

            // Wrap around horizontally
            if (p.x < 0) p.x = canvas.width;
            if (p.x > canvas.width) p.x = 0;
        });

        requestAnimationFrame(animate);
    }

    animate();
}

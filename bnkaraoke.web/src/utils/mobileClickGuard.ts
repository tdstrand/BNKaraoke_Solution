/**
 * Mobile-only click guard to prevent accidental clicks while scrolling.
 * Suppresses click events that occur immediately after a touch/pointer move
 * beyond a small threshold. Desktop unaffected.
 */
export function installMobileClickGuard(options?: { moveThreshold?: number; timeWindowMs?: number }) {
    if (typeof window === 'undefined' || typeof document === 'undefined') return;

    const n = navigator as Navigator & { maxTouchPoints?: number };
    const hasTouchCap = 'ontouchstart' in window || (typeof n.maxTouchPoints === 'number' && n.maxTouchPoints > 0);
    const isMobileUA = /mobile|android|iphone|ipad|ipod/i.test(navigator.userAgent);
    const isMobile = hasTouchCap || isMobileUA;
    if (!isMobile) return;

    const moveThreshold = options?.moveThreshold ?? 10; // px
    const timeWindowMs = options?.timeWindowMs ?? 250; // ms after last move to suppress click

    let startX = 0;
    let startY = 0;
    let moved = false;
    let lastMoveTime = 0;

    const onPointerDown = (e: PointerEvent) => {
        if (!e.isPrimary) return;
        if (e.pointerType === 'mouse') return; // ignore desktop mouse
        startX = e.clientX;
        startY = e.clientY;
        moved = false;
    };

    const onPointerMove = (e: PointerEvent) => {
        if (!e.isPrimary) return;
        if (e.pointerType === 'mouse') return;
        const dx = e.clientX - startX;
        const dy = e.clientY - startY;
        if (!moved && Math.hypot(dx, dy) > moveThreshold) {
            moved = true;
        }
        if (moved) {
            lastMoveTime = Date.now();
        }
    };

    const onClickCapture = (e: MouseEvent) => {
        // Allow developers to opt-out for specific elements
        const el = e.target as HTMLElement | null;
        if (el && el.closest && el.closest('[data-skip-click-guard]')) {
            moved = false;
            return;
        }
        // If we moved recently, this click likely came from a scroll gesture
        if (moved && Date.now() - lastMoveTime < timeWindowMs) {
            e.stopPropagation();
            e.preventDefault();
            moved = false; // reset after suppression
        }
    };

    // Fallback for older iOS Safari: touch events
    let tStartX = 0;
    let tStartY = 0;
    const onTouchStart = (e: TouchEvent) => {
        const t = e.touches[0];
        if (!t) return;
        tStartX = t.clientX;
        tStartY = t.clientY;
        moved = false;
    };
    const onTouchMove = (e: TouchEvent) => {
        const t = e.touches[0];
        if (!t) return;
        const dx = t.clientX - tStartX;
        const dy = t.clientY - tStartY;
        if (!moved && Math.hypot(dx, dy) > moveThreshold) {
            moved = true;
        }
        if (moved) {
            lastMoveTime = Date.now();
        }
    };

    // Register listeners
    window.addEventListener('pointerdown', onPointerDown, { passive: true });
    window.addEventListener('pointermove', onPointerMove, { passive: true });
    // Capture click at document to suppress early
    document.addEventListener('click', onClickCapture, true);

    // Touch fallbacks
    window.addEventListener('touchstart', onTouchStart, { passive: true });
    window.addEventListener('touchmove', onTouchMove, { passive: true });
}

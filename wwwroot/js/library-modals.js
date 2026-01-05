// Library Modal Functions
export function showAddLibraryModal() {
    const modal = document.getElementById('addLibraryModal');
    if (modal) modal.style.display = 'block';
}

export function closeAddLibraryModal() {
    const modal = document.getElementById('addLibraryModal');
    if (modal) modal.style.display = 'none';
}

// Export to window for onclick handlers
window.showAddLibraryModal = showAddLibraryModal;
window.closeAddLibraryModal = closeAddLibraryModal;


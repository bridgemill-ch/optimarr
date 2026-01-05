// Navigation and Tab Management
import { loadKnownLibraries } from './library.js';
import { loadBrowseFilterOptions, loadBrowseMedia, setupBrowseEventListeners } from './browse.js';
import { loadRatingSettings, loadCompatibilitySettings } from './settings.js';
import { loadDashboard } from './dashboard.js';

let switchTabFunction = null;

export function initNavigation() {
    // Mobile menu toggle
    const mobileMenuToggle = document.getElementById('mobileMenuToggle');
    const sidebar = document.getElementById('sidebar');
    const mobileOverlay = document.getElementById('mobileOverlay');
    
    function toggleMobileMenu() {
        if (sidebar && mobileOverlay && mobileMenuToggle) {
            sidebar.classList.toggle('open');
            mobileOverlay.classList.toggle('active');
            mobileMenuToggle.classList.toggle('active');
            document.body.style.overflow = sidebar.classList.contains('open') ? 'hidden' : '';
        }
    }
    
    if (mobileMenuToggle) {
        mobileMenuToggle.addEventListener('click', toggleMobileMenu);
    }
    
    if (mobileOverlay) {
        mobileOverlay.addEventListener('click', toggleMobileMenu);
    }
    
    // Tab switching
    const navItems = document.querySelectorAll('.nav-item');
    const contentSections = document.querySelectorAll('.content-section');

    function switchTab(targetTab, source) {
        if (!targetTab) {
            console.error('No target tab provided');
            return;
        }

        // Update active nav item
        navItems.forEach(n => n.classList.remove('active'));
        const activeNavItem = document.querySelector(`.nav-item[data-tab="${targetTab}"]`);
        if (activeNavItem) {
            activeNavItem.classList.add('active');
        }

        // Hide all sections
        contentSections.forEach(section => {
            section.classList.remove('active');
        });
        
        // Show target section
        const targetSection = document.getElementById(targetTab);
        if (targetSection) {
            targetSection.classList.add('active');
            
            // Force a reflow to ensure CSS applies
            void targetSection.offsetHeight;
            
            const computedDisplay = window.getComputedStyle(targetSection).display;
            
            // Fallback: if CSS didn't apply, force it with inline style
            if (computedDisplay === 'none') {
                targetSection.style.setProperty('display', 'block', 'important');
            }
            
            // Load data when switching tabs
            if (targetTab === 'dashboard') {
                loadDashboard();
            } else if (targetTab === 'library') {
                loadKnownLibraries();
            } else if (targetTab === 'browse') {
                // Load filters first, then media
                loadBrowseFilterOptions();
                loadBrowseMedia();
                // Setup event listeners for auto-search and sort changes
                setupBrowseEventListeners();
            } else if (targetTab === 'settings') {
                loadRatingSettings();
                loadCompatibilitySettings();
            }
        } else {
            console.error('Target section not found:', targetTab);
        }
        
        // Close mobile menu after navigation
        if (window.innerWidth <= 640 && sidebar && mobileOverlay && mobileMenuToggle) {
            sidebar.classList.remove('open');
            mobileOverlay.classList.remove('active');
            mobileMenuToggle.classList.remove('active');
            document.body.style.overflow = '';
        }
    }
    
    switchTabFunction = switchTab;

    navItems.forEach(item => {
        item.addEventListener('click', function(e) {
            e.preventDefault();
            e.stopPropagation();
            const targetTab = this.getAttribute('data-tab');
            switchTab(targetTab, 'click');
        });
    });
    
    // Also handle hash changes (for direct URL navigation)
    let hashChangeHandled = false;
    window.addEventListener('hashchange', function() {
        if (hashChangeHandled) return;
        hashChangeHandled = true;
        const hash = window.location.hash.substring(1); // Remove #
        if (hash) {
            switchTab(hash, 'hashchange');
        }
        setTimeout(() => { hashChangeHandled = false; }, 100);
    });
    
    // Handle initial hash on page load
    setTimeout(function() {
        if (window.location.hash) {
            const hash = window.location.hash.substring(1);
            switchTab(hash, 'initial');
        }
    }, 100);

    // Error toggle button
    const toggleErrorsBtn = document.getElementById('toggleErrors');
    if (toggleErrorsBtn) {
        toggleErrorsBtn.addEventListener('click', function() {
            const errorsList = document.getElementById('errorsList');
            if (errorsList) {
                const isVisible = errorsList.style.display !== 'none';
                errorsList.style.display = isVisible ? 'none' : 'block';
                this.textContent = isVisible ? 'Show' : 'Hide';
            }
        });
    }
}

export function switchTab(targetTab, source) {
    if (switchTabFunction) {
        switchTabFunction(targetTab, source);
    }
}


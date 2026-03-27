export {};

/// <reference path="globals.d.ts" />

// ── Types ───────────────────────────────────────────────────────────
interface PackagingRunRequest {
    sourceType: string;
    releaseFolderPath: string;
    createIntuneApp: boolean;
    uploadId?: string;
}

interface PackagingRunResponse {
    id: string;
    status: string;
}

interface UploadResponse {
    uploadId: string;
    files: { name: string; size: number; blobPath: string }[];
}

interface ApiErrorResponse {
    error?: string;
    message?: string;
}

// ── Constants ───────────────────────────────────────────────────────
const MAX_TOTAL_SIZE = 500 * 1024 * 1024; // 500 MB
const MAX_FILE_SIZE = 250 * 1024 * 1024; // 250 MB per file
const ALLOWED_EXTENSIONS = ['.exe', '.msi', '.zip', '.json'];
const SUBMIT_TIMEOUT_MS = 60_000;

// ── State ───────────────────────────────────────────────────────────
let createIntuneApp = true;
let isSubmitting = false;
let selectedFiles: File[] = [];

// ── DOM helpers ─────────────────────────────────────────────────────
function getEl<T extends HTMLElement>(id: string): T | null {
    return document.getElementById(id) as T | null;
}

function showElement(el: HTMLElement | null): void {
    el?.classList.remove('hidden');
}

function hideElement(el: HTMLElement | null): void {
    el?.classList.add('hidden');
}

// ── Toggle state ────────────────────────────────────────────────────
function initToggle(): void {
    const toggle = getEl<HTMLButtonElement>('createIntuneAppToggle');
    if (!toggle) return;

    toggle.addEventListener('click', () => {
        createIntuneApp = !createIntuneApp;
        toggle.setAttribute('aria-checked', String(createIntuneApp));

        const knob = toggle.querySelector('span');
        if (createIntuneApp) {
            toggle.classList.add('bg-primary');
            toggle.classList.remove('bg-neutral-200');
            knob?.classList.add('translate-x-5');
            knob?.classList.remove('translate-x-0');
        } else {
            toggle.classList.remove('bg-primary');
            toggle.classList.add('bg-neutral-200');
            knob?.classList.remove('translate-x-5');
            knob?.classList.add('translate-x-0');
        }
    });
}

// ── Source type switching ────────────────────────────────────────────
function initSourceTypeSwitch(): void {
    const sourceType = getEl<HTMLSelectElement>('sourceType');
    const folderPathSection = getEl('folderPathSection');
    const uploadSection = getEl('uploadSection');

    sourceType?.addEventListener('change', () => {
        hideElement(getEl('sourceTypeError'));
        sourceType.classList.remove('border-red-300');

        if (sourceType.value === 'local-upload') {
            hideElement(folderPathSection);
            showElement(uploadSection);
        } else {
            showElement(folderPathSection);
            hideElement(uploadSection);
        }
    });
}

// ── File upload UI ──────────────────────────────────────────────────
function initFileUpload(): void {
    const dropZone = getEl('dropZone');
    const fileInput = getEl<HTMLInputElement>('fileInput');

    if (!dropZone || !fileInput) return;

    // Click to browse
    dropZone.addEventListener('click', () => fileInput.click());

    // File input change
    fileInput.addEventListener('change', () => {
        if (fileInput.files) {
            addFiles(Array.from(fileInput.files));
            fileInput.value = ''; // reset so re-selecting same file triggers change
        }
    });

    // Drag & drop
    dropZone.addEventListener('dragover', (e: DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.add('border-primary', 'bg-primary/5');
        dropZone.classList.remove('border-neutral-300');
    });

    dropZone.addEventListener('dragleave', (e: DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.remove('border-primary', 'bg-primary/5');
        dropZone.classList.add('border-neutral-300');
    });

    dropZone.addEventListener('drop', (e: DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        dropZone.classList.remove('border-primary', 'bg-primary/5');
        dropZone.classList.add('border-neutral-300');

        if (e.dataTransfer?.files) {
            addFiles(Array.from(e.dataTransfer.files));
        }
    });
}

function addFiles(files: File[]): void {
    const uploadError = getEl('uploadError');
    hideElement(uploadError);

    for (const file of files) {
        const ext = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();
        if (!ALLOWED_EXTENSIONS.includes(ext)) {
            if (uploadError) {
                uploadError.textContent = `"${file.name}" has an unsupported file type. Allowed: ${ALLOWED_EXTENSIONS.join(', ')}`;
                showElement(uploadError);
            }
            return;
        }
        if (file.size > MAX_FILE_SIZE) {
            if (uploadError) {
                uploadError.textContent = `"${file.name}" exceeds the 250 MB per-file limit.`;
                showElement(uploadError);
            }
            return;
        }
        // Avoid duplicates
        if (!selectedFiles.some(f => f.name === file.name && f.size === file.size)) {
            selectedFiles.push(file);
        }
    }

    const totalSize = selectedFiles.reduce((sum, f) => sum + f.size, 0);
    if (totalSize > MAX_TOTAL_SIZE) {
        if (uploadError) {
            uploadError.textContent = `Total file size exceeds 500 MB (${formatSize(totalSize)}). Please remove some files.`;
            showElement(uploadError);
        }
    }

    if (selectedFiles.length > 10) {
        if (uploadError) {
            uploadError.textContent = 'Maximum 10 files allowed.';
            showElement(uploadError);
        }
        selectedFiles = selectedFiles.slice(0, 10);
    }

    renderFileList();
}

function removeFile(index: number): void {
    selectedFiles.splice(index, 1);
    hideElement(getEl('uploadError'));
    renderFileList();
}

function renderFileList(): void {
    const fileListEl = getEl('fileList');
    if (!fileListEl) return;

    if (selectedFiles.length === 0) {
        hideElement(fileListEl);
        fileListEl.innerHTML = '';
        return;
    }

    showElement(fileListEl);
    fileListEl.innerHTML = selectedFiles.map((file, i) => `
        <div class="flex items-center justify-between bg-neutral-50 rounded-lg px-3 py-2 border border-neutral-200">
            <div class="flex items-center gap-2 min-w-0">
                <svg viewBox="0 0 24 24" class="h-4 w-4 text-neutral-400 flex-shrink-0" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"></path>
                    <path d="M14 2v6h6"></path>
                </svg>
                <span class="text-sm text-neutral-700 truncate">${escapeHtml(file.name)}</span>
                <span class="text-xs text-neutral-400 flex-shrink-0">${formatSize(file.size)}</span>
            </div>
            <button type="button" data-remove-index="${i}" class="text-neutral-400 hover:text-red-500 transition flex-shrink-0 ml-2" aria-label="Remove file">
                <svg viewBox="0 0 24 24" class="h-4 w-4" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M6 6l12 12M18 6L6 18"></path>
                </svg>
            </button>
        </div>
    `).join('');

    // Wire remove buttons
    fileListEl.querySelectorAll<HTMLButtonElement>('[data-remove-index]').forEach(btn => {
        btn.addEventListener('click', () => {
            const idx = parseInt(btn.getAttribute('data-remove-index') || '0', 10);
            removeFile(idx);
        });
    });
}

function formatSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
}

function escapeHtml(text: string): string {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// ── Path format validation ──────────────────────────────────────────
function validatePathFormat(path: string, sourceType: string): string | null {
    if (sourceType === 'fileshare') {
        // UNC path: \\server\share\... or drive path: C:\...
        if (/^\\\\[^\\]+\\[^\\]+/.test(path) || /^[A-Za-z]:\\/.test(path)) {
            return null;
        }
        return 'Expected a UNC path (\\\\server\\share\\...) or drive path (C:\\...).';
    }
    if (sourceType === 'blob') {
        // Blob URL: https://...blob.core.windows.net/...
        if (/^https?:\/\/[^/]+\.blob\.core\.windows\.net\/.+/.test(path)) {
            return null;
        }
        return 'Expected an Azure Blob Storage URL (https://account.blob.core.windows.net/...).';
    }
    return null;
}

// ── Validation ──────────────────────────────────────────────────────
function validateForm(): boolean {
    let valid = true;

    const sourceType = getEl<HTMLSelectElement>('sourceType');
    const sourceTypeError = getEl('sourceTypeError');
    const folderPath = getEl<HTMLInputElement>('releaseFolderPath');
    const folderPathError = getEl('releaseFolderPathError');
    const pathFormatError = getEl('pathFormatError');
    const uploadError = getEl('uploadError');

    // Source type
    if (!sourceType?.value) {
        showElement(sourceTypeError);
        sourceType?.classList.add('border-red-300');
        valid = false;
    } else {
        hideElement(sourceTypeError);
        sourceType?.classList.remove('border-red-300');
    }

    if (sourceType?.value === 'local-upload') {
        // Validate file selection
        if (selectedFiles.length === 0) {
            if (uploadError) {
                uploadError.textContent = 'Please select at least one file to upload.';
                showElement(uploadError);
            }
            valid = false;
        }
        const totalSize = selectedFiles.reduce((sum, f) => sum + f.size, 0);
        if (totalSize > MAX_TOTAL_SIZE) {
            valid = false;
        }
    } else {
        // Validate release folder path
        if (!folderPath?.value.trim()) {
            showElement(folderPathError);
            folderPath?.classList.add('border-red-300');
            valid = false;
        } else {
            hideElement(folderPathError);
            folderPath?.classList.remove('border-red-300');

            // Path format validation
            const formatErr = validatePathFormat(folderPath.value.trim(), sourceType?.value || '');
            if (formatErr) {
                if (pathFormatError) {
                    pathFormatError.textContent = formatErr;
                    showElement(pathFormatError);
                }
                valid = false;
            } else {
                hideElement(pathFormatError);
            }
        }
    }

    return valid;
}

// ── Status area management ──────────────────────────────────────────
function showStatus(state: 'loading' | 'success' | 'error', message?: string): void {
    const statusArea = getEl('statusArea');
    const loading = getEl('loadingState');
    const success = getEl('successState');
    const error = getEl('errorState');

    showElement(statusArea);
    hideElement(loading);
    hideElement(success);
    hideElement(error);

    if (state === 'loading') {
        const loadingText = loading?.querySelector('p');
        if (loadingText && message) loadingText.textContent = message;
        showElement(loading);
    }
    if (state === 'success') showElement(success);
    if (state === 'error') showElement(error);
}

function hideStatus(): void {
    hideElement(getEl('statusArea'));
}

// ── Upload files to server ──────────────────────────────────────────
function uploadFiles(): Promise<UploadResponse> {
    return new Promise((resolve, reject) => {
        const formData = new FormData();
        for (const file of selectedFiles) {
            formData.append('files', file);
        }

        const xhr = new XMLHttpRequest();
        xhr.open('POST', '/api/packaging/upload');

        const progressBar = getEl('uploadProgressBar');
        const progressText = getEl('uploadProgressText');
        const progressContainer = getEl('uploadProgress');
        showElement(progressContainer);

        xhr.upload.addEventListener('progress', (e: ProgressEvent) => {
            if (e.lengthComputable) {
                const pct = Math.round((e.loaded / e.total) * 100);
                if (progressBar) progressBar.style.width = pct + '%';
                if (progressText) progressText.textContent = pct + '%';
            }
        });

        xhr.addEventListener('load', () => {
            hideElement(progressContainer);
            if (xhr.status >= 200 && xhr.status < 300) {
                try {
                    resolve(JSON.parse(xhr.responseText));
                } catch {
                    reject(new Error('Invalid response from upload endpoint.'));
                }
            } else {
                let msg = `Upload failed (HTTP ${xhr.status})`;
                try {
                    const data = JSON.parse(xhr.responseText);
                    msg = data.error || msg;
                } catch { /* ignore */ }
                reject(new Error(msg));
            }
        });

        xhr.addEventListener('error', () => {
            hideElement(progressContainer);
            reject(new Error('Network error during file upload.'));
        });

        xhr.addEventListener('abort', () => {
            hideElement(progressContainer);
            reject(new Error('Upload was cancelled.'));
        });

        xhr.send(formData);
    });
}

// ── Form submission ─────────────────────────────────────────────────
async function submitForm(): Promise<void> {
    if (isSubmitting) return;
    if (!validateForm()) return;

    // Confirmation dialog
    if (!window.confirm('Start this packaging run?')) return;

    isSubmitting = true;
    const sourceType = getEl<HTMLSelectElement>('sourceType')!.value;
    const submitBtn = getEl<HTMLButtonElement>('submitBtn');

    if (submitBtn) {
        submitBtn.disabled = true;
        submitBtn.classList.add('opacity-60', 'cursor-not-allowed');
    }

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), SUBMIT_TIMEOUT_MS);

    try {
        let uploadId: string | undefined;

        // Step 1: Upload files if local-upload
        if (sourceType === 'local-upload') {
            showStatus('loading', 'Uploading files…');
            const uploadResult = await uploadFiles();
            uploadId = uploadResult.uploadId;
        }

        // Step 2: Start packaging run
        showStatus('loading', 'Starting packaging run…');

        const releaseFolderPath = sourceType === 'local-upload'
            ? ''
            : getEl<HTMLInputElement>('releaseFolderPath')!.value.trim();

        const payload: PackagingRunRequest = {
            sourceType,
            releaseFolderPath,
            createIntuneApp
        };
        if (uploadId) {
            payload.uploadId = uploadId;
        }

        const response = await fetch('/api/packaging/run', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
            signal: controller.signal
        });

        if (!response.ok) {
            let errorMsg = 'An unexpected error occurred. Please try again.';
            try {
                const errorData: ApiErrorResponse = await response.json();
                errorMsg = errorData.error || errorData.message || errorMsg;
            } catch {
                // Response body was not JSON
            }
            const errorMessageEl = getEl('errorMessage');
            if (errorMessageEl) errorMessageEl.textContent = errorMsg;
            showStatus('error');
            return;
        }

        const data: PackagingRunResponse = await response.json();
        const runDetailLink = getEl<HTMLAnchorElement>('runDetailLink');
        if (runDetailLink && data.id) {
            runDetailLink.href = `/app/run-detail.html?id=${encodeURIComponent(data.id)}`;
        }
        showStatus('success');

        // Clear selected files on success
        selectedFiles = [];
        renderFileList();

    } catch (error) {
        const errorMessageEl = getEl('errorMessage');
        if (errorMessageEl) {
            if (error instanceof DOMException && error.name === 'AbortError') {
                errorMessageEl.textContent = 'Request timed out. The server may still be processing. Check the Runs page.';
            } else {
                errorMessageEl.textContent = error instanceof Error
                    ? error.message
                    : 'Could not connect to the server. Please check your connection and try again.';
            }
        }
        showStatus('error');
    } finally {
        clearTimeout(timeoutId);
        isSubmitting = false;
        if (submitBtn) {
            submitBtn.disabled = false;
            submitBtn.classList.remove('opacity-60', 'cursor-not-allowed');
        }
    }
}

// ── Initialisation ──────────────────────────────────────────────────
function initNewRunForm(): void {
    initToggle();
    initSourceTypeSwitch();
    initFileUpload();

    const form = getEl<HTMLFormElement>('newRunForm');
    form?.addEventListener('submit', (e: Event) => {
        e.preventDefault();
        hideStatus();
        submitForm();
    });

    // Dismiss error button
    const dismissError = getEl<HTMLButtonElement>('dismissError');
    dismissError?.addEventListener('click', () => hideStatus());

    // Clear validation errors on input change
    const folderPath = getEl<HTMLInputElement>('releaseFolderPath');
    folderPath?.addEventListener('input', () => {
        hideElement(getEl('releaseFolderPathError'));
        hideElement(getEl('pathFormatError'));
        folderPath.classList.remove('border-red-300');
    });
}

// Wait for app shell (auth) to be ready before initialising
if (window.appShellReady) {
    initNewRunForm();
} else {
    window.addEventListener('app-shell-ready', () => initNewRunForm());
}

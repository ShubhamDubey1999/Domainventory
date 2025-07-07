'use strict';
function generateRequestId() {
    return 'xxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        const r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

var requestId;
const pollInterval = 2000;

async function pollProgress() {
    try {
        const resp = await fetch(`/Domainventory/GetProgress?requestId=${requestId}`);
        if (!resp.ok) { console.error("Failed to get progress"); return; }

        const { processed, total } = await resp.json();

        // numbers & %
        const percent = total ? Math.floor((processed / total) * 100) : 0;
        document.getElementById("domains-checked").textContent = processed;
        document.getElementById("domains-length").textContent = total;
        document.getElementById("progress").textContent = percent;

        // progress‑bar width + aria
        const bar = document.getElementById("progress-bar");
        bar.style.width = `${percent}%`;
        bar.ariaValueNow = percent;

        updateStopwatch();
        if (processed < total) {
            setTimeout(pollProgress, pollInterval);
        }
        // no overlay to hide – finished quietly
    } catch (err) {
        console.error("Error polling progress:", err);
    }
}
let startTime = null;   // Date.now() when we started/resumed
let elapsedBeforePause = 0;      // ms already counted before the pause
let stopwatchInterval = null;   // setInterval handle
function updateStopwatch() {
    // total elapsed = time from previous runs + time since last resume
    const totalMs = elapsedBeforePause + (startTime ? Date.now() - startTime : 0);
    const secs = Math.floor(totalMs / 1000);
    const mm = String(Math.floor(secs / 60)).padStart(2, "0");
    const ss = String(secs % 60).padStart(2, "0");
    document.getElementById("stopwatch").textContent = `⏱ ${mm}:${ss}`;
}


$(document).ready(function () {
    $(".js-select2").select2({
        placeholder: "Choose TLD extensions",
    });
    $("#domainTextarea").on("keydown", function (e) {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            $("form").submit();
        }
    });

    document.getElementById("tableSearch").addEventListener("keyup", applyTableFilter);
    let currentPage = 1;
    let rowsPerPage = 50;
    let fileName = $("#fileName").val();

    $('#prevPage').click(() => {
        if (currentPage > 1) {
            currentPage--;
            fileName = $("#fileName").val();
            rowsPerPage = $("#rowsPerPage").val();
            loadPage(fileName, currentPage, rowsPerPage);
            $('#currentPage').text(currentPage);
        }
    });

    $('#nextPage').click(() => {
        currentPage++;
        fileName = $("#fileName").val();
        rowsPerPage = $("#rowsPerPage").val();
        loadPage(fileName, currentPage, rowsPerPage);
        $('#currentPage').text(currentPage);
    });
    $('#resetbtn').click(() => {
        reset();
    });

    $('#rowsPerPage').change(function () {
        let selectedValue = $(this).val();
        let pageSize = selectedValue === "all" ? "all" : parseInt(selectedValue);
        currentPage = 1;
        fileName = $("#fileName").val();
        loadPage(fileName, currentPage, pageSize);
        $('#currentPage').text(currentPage);
    });
    //$("#runAiSearch").click(function () {
    //    const prompt = $("#aiPrompt").val().trim();
    //    const responseBox = $("#aiResponse");

    //    responseBox.html("<p class='text-warning'>🧠 Thinking...</p>");

    //    if (!prompt) {
    //        alert("Please enter a prompt.");
    //        responseBox.html("<em class='text-muted'>Response will appear here...</em>");
    //        return;
    //    }

    //    $.ajax({
    //        url: "Domainventory/AISuggestDomains?prompt=" + encodeURIComponent(prompt),
    //        type: "GET",
    //        success: function (data) {
    //            responseBox.empty();

    //            if (data.suggestions && data.suggestions.length > 0) {
    //                const row = $("<div>").addClass("row");

    //                data.suggestions.forEach(function (domain) {
    //                    const col = $("<div>").addClass("col-md-4 mb-2");

    //                    const link = $("<a>")
    //                        .attr("href", "https://" + domain)
    //                        .attr("target", "_blank")
    //                        .text(domain)
    //                        .css({
    //                            color: "#11AA11",
    //                            backgroundColor: "#2a2a2a",
    //                            display: "block",
    //                            padding: "8px 12px",
    //                            borderRadius: "5px",
    //                            textDecoration: "none",
    //                            fontSize: "14px"
    //                        });

    //                    col.append(link);
    //                    row.append(col);
    //                });

    //                responseBox.append(row);
    //            } else {
    //                responseBox.html("<p class='text-warning'>⚠️ No suggestions found.</p>");
    //            }
    //        },
    //        error: function (xhr) {
    //            responseBox.html("<p class='text-danger'>❌ Error: " + xhr.responseText + "</p>");
    //        }
    //    });

    //});
    //$("#aiSearchModal").on("hidden.bs.modal", function () {
    //    $("#aiPrompt").val("");
    //    $("#aiResponse").html("<em class='text-muted'>Response will appear here...</em>");
    //});

    //loadPage(fileName, currentPage, rowsPerPage);

    $('#availability-form').on('submit', function (e) {
        e.preventDefault();
        $('#result-section').hide();
        //const overlay = document.getElementById("overlay-loader");
        //const progressFill = document.getElementById("loader-progress-text");
        requestId = generateRequestId();

        $('#result-table tbody').empty();
        $('#available-counter').text(0);
        $('#unavailable-counter').text(0);
        $('#error-counter').text(0);
        $('#domains-checked').text(0);
        $('#domains-length').text(0);
        $('#progress').text(0);


        let domainsInput = $('textarea[name="domains"]').val();
        let domainList = domainsInput
            .split(/[\s\n]+/)
            .filter(d => d.trim().length > 0)
            .map(d => d.trim());
        if (domainList.length == 0) {
            alert("No domains provided.");
            return false;
        }
        let tlds = $('select[name="tlds"]').val();
        //overlay.classList.add("active");
        //progressFill.style.width = "0%";
        $('#progress').text(0);                 // existing line

        $('#progress-bar')                      // NEW – zero the bar itself
            .css('width', '0%')
            .attr('aria-valuenow', 0);
        startTime = Date.now();                 // NEW – restart the stopwatch (if you use it)

        $('#result-section').show();
        document.querySelector("#result-section").scrollIntoView({
            behavior: "smooth"
        });
        let requestPayload = {
            domains: domainList,
            tlds: tlds,
            suffix: $('input[name="suffix"]').val(),
            prefix: $('input[name="prefix"]').val(),
            maxlength: parseInt($('input[name="max-length"]').val()) || 0,
        };

        pollProgress();
        $.ajax({
            url: 'Domainventory/CheckDomains?requestId=' + requestId,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(requestPayload),
            success: function (response) {
                console.log(response);
                $('#domains-length').text(response.total);
                $('#domains-checked').text(response.results.length);
                $('#available-counter').text(response.available);
                $('#unavailable-counter').text(response.unavailable);
                $('#error-counter').text(response.error);
                $('#progress').text(Math.floor((response.results.length / response.total) * 100));
                $("#fileName").val(response.csvFileName);
                $('#stopwatch').text(response.timeTakenInSeconds);
                currentPage = 1;
                rowsPerPage = parseInt($('#rowsPerPage').val()) || 50;
                $('#currentPage').text(currentPage);
                loadPage(response.csvFileName, currentPage, rowsPerPage);

                //if (overlay.classList.contains("active")) {
                //    overlay.classList.remove("active");
                //    progressFill.style.width = "0%";
                //}
                $('#result-section').show();

                //document.querySelector("#result-section").scrollIntoView({
                //    behavior: "smooth"
                //});
            },
            error: function (xhr, status, error) {
                alert('Error: ' + xhr.responseText);
            }
        });
    });

    $('#hide-unavailable').change(function () {
        if (this.checked) {
            $('#result-table tbody tr').each(function () {
                const availability = $(this).find('td:nth-child(3)').text();
                if (availability.includes('Unavailable')) {
                    $(this).hide();
                }
            });
        } else {
            $('#result-table tbody tr').show();
        }
    });

    $('#hide-whois').change(function () {
        $('.clm-whois').toggle(!this.checked);
    });

    $('#hide-exec-time').change(function () {
        $('.clm-exec-time').toggle(!this.checked);
    });

    $('#hide-resource').change(function () {
        $('.clm-resource').toggle(!this.checked);
    });

    $('#hide-whois').trigger('change');
    $('#hide-exec-time').trigger('change');
    $('#hide-resource').trigger('change');
    $('#hide-unavailable').trigger('change');
});

function loadPage(fileName, pageNumber, pageSize = 50) {
    const isAll = pageSize === "all";

    $.ajax({
        url: `Domainventory/GetDomainResultsPage`,
        data: {
            relativeFilePath: fileName,
            pageNumber: isAll ? 1 : pageNumber,
            pageSize: isAll ? 0 : pageSize
        },
        success: function (response) {
            let tbody = $('#result-table tbody');
            tbody.empty();
            var newRows = "";
            response.results.forEach(row => {
                const isUnavailable = row.availability.includes("Unavailable");
                const isError = row.availability.toLowerCase().includes("error") || row.availability.toLowerCase().includes("invalid");
                const rowClass = (isUnavailable || isError) ? 'bg-danger' : 'bg-success';
                newRows += `
            <tr class="${rowClass}">
                <td>${row.id}</td>
                <td>${row.domain}</td>
                <td>${isUnavailable ? 'Unavailable' : isError ? row.availability : 'Available'}</td>
                <td>${row.length}</td>
                <td>${row.message}</td>
                <td class="clm-whois">${row.whoisServer}</td>
                <td class="clm-resource">${row.resource}</td>
                <td class="clm-exec-time">${row.executeTime}</td>
                <td>${row.actions || '—'}</td>
            </tr>
        `
            });
            tbody.append(newRows);

            if (isAll) {
                $('#paginationControls').hide();
            } else {
                $('#paginationControls').show();
                $('#currentPage').text(pageNumber);
                $('#totalPages').text(response.totalPages);
                $('#prevPage').prop('disabled', pageNumber <= 1);
                $('#nextPage').prop('disabled', pageNumber >= response.totalPages);
            }
            applyTableFilter();
            $('#hide-whois').trigger('change');
            $('#hide-exec-time').trigger('change');
            $('#hide-resource').trigger('change');
            $('#hide-unavailable').trigger('change');
        },
        error: function (xhr, status, error) {
            alert('Failed to load page data: ' + error);
        }
    });
}

function ImportFile() {
    document.getElementById("importFile").click();
}

function uploadFile() {
    const fileInput = document.getElementById("importFile");
    if (!fileInput.files.length) {
        alert("Please select a file first");
        return;
    }

    const formData = new FormData();
    formData.append("domainFile", fileInput.files[0]);

    fetch("/Home/ImportDomains", {
        method: "POST",
        body: formData
    })
        .then(response => {
            if (!response.ok) throw new Error("File upload failed");
            return response.json();
        })
        .then(data => {
            document.getElementById("domainTextarea").value = data.domains;
            document.getElementById('stopwatch').textContent = '00:00';
        })
        .catch(error => alert("Error: " + error.message));
}

function DownloadSample() {
    window.location.href = '/sample/DomainName.xlsx';
}

document.getElementById("export-csv").addEventListener("click", function () {
    const fileName = document.getElementById("fileName").value;
    if (!fileName) {
        alert("No file found to export.");
        return;
    }

    window.location.href = `Domainventory/DownloadCsv?fileName=${encodeURIComponent(fileName)}`;
});


document.getElementById("export-excel").addEventListener("click", function () {
    const fileName = document.getElementById("fileName").value;
    if (!fileName) {
        alert("No file found to export.");
        return;
    }

    window.location.href = `Domainventory/DownloadExcel?csvFilePath=${encodeURIComponent(fileName)}`;
});
document.getElementById("export-txt").addEventListener("click", function () {
    const fileName = document.getElementById("fileName").value;
    if (!fileName) {
        alert("No file found to export.");
        return;
    }
    downloadTxtFromCsv(fileName);
});
document.getElementById("export-json").addEventListener("click", function () {
    const fileName = document.getElementById("fileName").value;
    if (!fileName) {
        alert("No file found to export.");
        return;
    }
    downloadJsonFromCSV(fileName);
});

function downloadTxtFromCsv(filePath) {
    const url = "Domainventory/DownloadTxt?filePath=" + encodeURIComponent(filePath);
    const a = document.createElement("a");
    a.href = url;
    a.download = ""; // Let the server control the filename
    document.body.appendChild(a);
    a.click();
    a.remove();
}

function downloadJsonFromCSV(filePath) {
    const url = "Domainventory/DownloadJSON?filePath=" + encodeURIComponent(filePath);
    const a = document.createElement("a");
    a.href = url;
    a.download = "";
    document.body.appendChild(a);
    a.click();
    a.remove();
}


function applyTableFilter() {
    const filter = document.getElementById("tableSearch").value.toLowerCase();
    const rows = document.querySelectorAll("#result-table tbody tr");

    rows.forEach(row => {
        const cells = row.getElementsByTagName("td");
        let matchFound = false;

        for (let i = 0; i < cells.length; i++) {
            const cellText = cells[i].textContent || cells[i].innerText;
            if (cellText.toLowerCase().includes(filter)) {
                matchFound = true;
                break;
            }
        }

        row.style.display = matchFound ? "" : "none";
    });
}
function openModal() {
    document.getElementById("domainModal").classList.remove("hidden");
    document.getElementById("domainModal").classList.add("modal-overlay");
}

function closeModal() {
    document.getElementById("domainModal").classList.add("hidden");
    document.getElementById("domainModal").classList.remove("modal-overlay");
}

function verifyDomain() {
    const domain = document.getElementById("domainInput").value;
    alert("Verifying: " + domain);
    // You can hook this into your backend later
}
function SearchAvaialbleDomain() {
    const domain = $("#domainInput").val().trim();
    const resultDiv = $("#domainResult");
    const suggestionsList = $("#suggestedDomains");

    suggestionsList.empty();

    if (!domain) {
        alert("Please enter a domain.");
        return;
    }

    resultDiv.html("");
    resultDiv.html("<p class='text-warning'>🔍 Verifying...</p>");

    $.ajax({
        url: "Domainventory/SuggestedAvailableDomains?domain=" + encodeURIComponent(domain),
        type: 'GET',
        success: function (response) {
            resultDiv.empty();
            suggestionsList.empty();

            // Show availability status
            const status = $("<p>")
                .addClass(response.available ? "text-success" : "text-danger")
                .html(response.available ? "✅ Domain is available!" : "❌ Domain is not available.");
            resultDiv.append(status);

            // Show suggestions in 3-column layout
            if (response.suggestions && response.suggestions.length > 0) {
                response.suggestions.forEach(function (s) {
                    const col = $("<div>").addClass("col-md-4 mb-2");

                    const link = $("<a>")
                        .attr("href", "https://" + s)
                        .attr("target", "_blank")
                        .text(s)
                        .css({
                            color: "#11AA11",
                            backgroundColor: "#2a2a2a",
                            display: "block",
                            padding: "8px 12px",
                            borderRadius: "5px",
                            textDecoration: "none",
                            fontSize: "14px"
                        });

                    col.append(link);
                    suggestionsList.append(col);
                });
            }
        },
        error: function (xhr) {
            resultDiv.html("<p class='text-danger'>❌ Error: " + xhr.responseText + "</p>");
        }
    });
}
function reset() {
    location.reload();
}
function RunAISearch() {
    const prompt = $("#domainTextarea").val().trim();
    //const responseBox = $("#aiResponse");

    //responseBox.html("<p class='text-warning'>🧠 Thinking...</p>");

    if (!prompt) {
        alert("Please enter a prompt.");
        //responseBox.html("<em class='text-muted'>Response will appear here...</em>");
        return;
    }

    $.ajax({
        url: "Domainventory/AISuggestDomains?prompt=" + encodeURIComponent(prompt),
        type: "GET",
        success: function (response) {
            $('#domains-length').text(response.total);
            $('#domains-checked').text(response.results.length);
            $('#available-counter').text(response.available);
            $('#unavailable-counter').text(response.unavailable);
            $('#error-counter').text(response.error);
            $('#progress').text(Math.floor((response.results.length / response.total) * 100));
            $("#fileName").val(response.csvFileName);
            $('#stopwatch').text(response.timeTakenInSeconds);
            const currentPage = 1;
            const rowsPerPage = parseInt($('#rowsPerPage').val()) || 50;
            $('#currentPage').text(currentPage);
            loadPage(response.csvFileName, currentPage, rowsPerPage);

            //if (overlay.classList.contains("active")) {
            //    overlay.classList.remove("active");
            //    progressFill.style.width = "0%";
            //}
            $('#result-section').show();

            document.querySelector("#result-section").scrollIntoView({
                behavior: "smooth"
            });
        },
        error: function (xhr) {
            //responseBox.html("<p class='text-danger'>❌ Error: " + xhr.responseText + "</p>");
        }
    });
}
function startStopwatch() {                 // call when the job begins
    startTime = Date.now();
    elapsedBeforePause = 0;
    clearInterval(stopwatchInterval);
    stopwatchInterval = setInterval(updateStopwatch, 1000);
}

function pauseStopwatch() {                 // local pause
    if (!startTime) return;                 // already paused
    elapsedBeforePause += Date.now() - startTime;
    startTime = null;
    clearInterval(stopwatchInterval);
    updateStopwatch();
}

function resumeStopwatch() {                // local resume
    if (startTime) return;                  // already running
    startTime = Date.now();
    clearInterval(stopwatchInterval);
    stopwatchInterval = setInterval(updateStopwatch, 1000);
}

function stopStopwatch() {                  // local stop
    startTime = null;
    elapsedBeforePause = 0;
    clearInterval(stopwatchInterval);
    updateStopwatch();                      // will show 00:00
}

/* ------------------------------------------------------------------ */
/*  API wrappers that mix UI + server calls                           */
/* ------------------------------------------------------------------ */
function pauseJob() {
    fetch(`/Domainventory/PauseJob?requestId=${requestId}`, { method: "POST" })
        .then(() => pauseStopwatch())
        .catch(console.error);
}

function resumeJob() {
    fetch(`/Domainventory/ResumeJob?requestId=${requestId}`, { method: "POST" })
        .then(() => resumeStopwatch())
        .catch(console.error);
}

function stopJob() {
    fetch(`/Domainventory/StopJob?requestId=${requestId}`, { method: "POST" })
        .then(() => stopStopwatch())
        .catch(console.error);
}

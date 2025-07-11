﻿'use strict';
function generateRequestId() {
    return 'xxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        const r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

const requestId = generateRequestId();
const pollInterval = 2000;

async function pollProgress() {
    try {
        const response = await fetch(`Domainventory/GetProgress?requestId=${requestId}`);
        if (!response.ok) {
            console.error("Failed to get progress.");
            return;
        }

        const data = await response.json();
        const { processed, total } = data;

        const percent = Math.floor((processed / total) * 100);
        document.getElementById("loader-progress-text").textContent = `${percent}%`;

        if (processed < total) {
            setTimeout(pollProgress, pollInterval);
        } else {
            document.getElementById("loader-progress-text").textContent = "Completed";
            // optionally hide the loader
            setTimeout(() => {
                document.getElementById("overlay-loader").classList.remove("active");
            }, 1000);
        }
    } catch (error) {
        console.error("Error polling progress:", error);
    }
}

$(document).ready(function () {
    document.getElementById("tableSearch").addEventListener("keyup", applyTableFilter);
    $('#tlds-select').select2({
        placeholder: "Select TLDs",
        width: '100%'
    });
    let currentPage = 1;
    let rowsPerPage = 50;
    let fileName = $("#fileName").val();

    $('#prevPage').click(() => {
        if (currentPage > 1) {
            currentPage--;
            fileName = $("#fileName").val();
            loadPage(fileName, currentPage, rowsPerPage);
            $('#currentPage').text(currentPage);
        }
    });

    $('#nextPage').click(() => {
        currentPage++;
        fileName = $("#fileName").val();
        loadPage(fileName, currentPage, rowsPerPage);
        $('#currentPage').text(currentPage);
    });

    $('#rowsPerPage').change(function () {
        let selectedValue = $(this).val();
        let pageSize = selectedValue === "all" ? "all" : parseInt(selectedValue);
        currentPage = 1;
        fileName = $("#fileName").val();
        loadPage(fileName, currentPage, pageSize);
        $('#currentPage').text(currentPage);
    });

    loadPage(fileName, currentPage, rowsPerPage);

    $('#availability-form').on('submit', function (e) {
        e.preventDefault();
        const overlay = document.getElementById("overlay-loader");
        const progressFill = document.getElementById("loader-progress-text");

        overlay.classList.add("active");
        progressFill.style.width = "0%";
        $('#result-table tbody').empty();
        $('#available-counter span').text(0);
        $('#unavailable-counter span').text(0);
        $('#error-counter span').text(0);
        $('#domains-checked').text(0);
        $('#domains-length').text(0);
        $('#progress').text(0);
        $('.result').removeClass('hidden');

        let domainsInput = $('textarea[name="domains"]').val();
        let domainList = domainsInput
            .split(/[\s\n]+/)
            .filter(d => d.trim().length > 0)
            .map(d => d.trim());

        let tlds = $('select[name="tlds"]').val();

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
                $('#available-counter span').text(response.available);
                $('#unavailable-counter span').text(response.unavailable);
                $('#error-counter span').text(response.error);
                $('#progress').text(Math.floor((response.results.length / response.total) * 100));
                $("#fileName").val(response.csvFileName);
                $('#stopwatch').text(response.timeTakenInSeconds);
                currentPage = 1;
                rowsPerPage = parseInt($('#rowsPerPage').val()) || 50;
                $('#currentPage').text(currentPage);
                loadPage(response.csvFileName, currentPage, rowsPerPage);

                if (overlay.classList.contains("active")) {
                    overlay.classList.remove("active");
                    progressFill.style.width = "0%";
                }
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

            response.results.forEach(row => {
                const isUnavailable = row.availability.includes("Unavailable");
                const rowClass = isUnavailable ? 'error' : '';

                tbody.append(`
            <tr class="${rowClass}">
                <td>${row.id}</td>
                <td>${row.domain}</td>
                <td>${isUnavailable ? 'Unavailable' : 'Available'}</td>
                <td>${row.length}</td>
                <td>${row.message}</td>
                <td class="clm-whois">${row.whoisServer}</td>
                <td class="clm-resource">${row.resource}</td>
                <td class="clm-exec-time">${row.executeTime}</td>
                <td>${row.actions || '—'}</td>
            </tr>
        `);
            });

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
    //$("#result-table").table2csv("download", { filename: "domain-results.csv" });
    const fileName = document.getElementById("fileName").value;
    if (!fileName) {
        alert("No CSV file found to export.");
        return;
    }

    window.location.href = `Domainventory/DownloadCsv?fileName=${encodeURIComponent(fileName)}`;
});

$('#export-excel').click(function () {
    let table = document.getElementById("result-table");
    let workbook = XLSX.utils.table_to_book(table, { sheet: "Domains" });
    XLSX.writeFile(workbook, "DomainResults.xlsx");
});
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
    const resultDiv = $("#verifyResult");
    resultDiv.html("");

    if (!domain) {
        alert("Please enter a domain.");
        return;
    }

    resultDiv.html("<p class='warning'>Verifying...</p>");

    $.ajax({
        url: "Domainventory/SuggestedAvailableDomains?domain=" + encodeURIComponent(domain),
        type: 'GET',
        success: function (response) {
            resultDiv.empty();
            console.log(response);

            // Show availability status
            const status = $("<p>")
                .addClass(response.available ? "success" : "error")
                .text(response.available ? "✅ Domain is available!" : "❌ Domain is not available.");
            resultDiv.append(status);

            // Show suggestions if any
            if (response.suggestions && response.suggestions.length > 0) {
                const title = $("<h4>").text("Suggested Domains:");
                resultDiv.append(title);

                const container = $("<div>").css({
                    display: "flex",
                    flexWrap: "wrap",
                    gap: "10px",
                    marginTop: "10px"
                });

                response.suggestions.forEach(function (s) {
                    const suggestionBox = $("<div>").css({
                        border: "1px solid #11AA11",
                        padding: "8px 12px",
                        borderRadius: "5px",
                        backgroundColor: "#111111",
                        color: "#11AA11",
                        fontSize: "13px"
                    });

                    const link = $("<a>")
                        .attr("href", "https://" + s)
                        .attr("target", "_blank")
                        .css({
                            color: "#11AA11",
                            textDecoration: "none"
                        })
                        .text(s);

                    suggestionBox.append(link);
                    container.append(suggestionBox);
                });

                resultDiv.append(container);
            }
        },
        error: function (xhr) {
            resultDiv.html("<p class='error'>Error: " + xhr.responseText + "</p>");
        }
    });
}


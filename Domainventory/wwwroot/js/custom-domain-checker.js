$(document).ready(function () {
    $('#TldsName').select2({
        placeholder: "Select Tlds",
        closeOnSelect: false
    });
    $('#result-table').DataTable({
        pageLength: 10,
        order: [[0, 'asc']], // Sort by first column (ID)
        columnDefs: [
            { targets: [5, 6, 7], visible: true }, // Optional: control visibility
            { targets: -1, orderable: false } // Actions column not sortable
        ]
    });
});
let stopwatchInterval;
let startTime;

function startStopwatch() {
    clearInterval(stopwatchInterval); // prevent multiple intervals
    startTime = new Date().getTime();

    stopwatchInterval = setInterval(() => {
        const elapsed = new Date().getTime() - startTime;
        const minutes = Math.floor(elapsed / 60000).toString().padStart(2, '0');
        const seconds = Math.floor((elapsed % 60000) / 1000).toString().padStart(2, '0');
        document.getElementById('stopwatch').textContent = `${minutes}:${seconds}`;
    }, 1000);
}

function stopStopwatch() {
    clearInterval(stopwatchInterval);
}

// SignalR connection and form submit
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/domainHub")
    .configureLogging(signalR.LogLevel.Information)
    .build();

let totalDomains = 0;

connection.on("DomainChecked", function (result) {
    const tbody = document.querySelector("#result-table tbody");
    const currentIndex = tbody.rows.length + 1;
    const row = document.createElement("tr");
    const parts = result.split(" - ");
    const domain = parts.shift();
    const status = parts.join(" - ");

    let statusClass = "", actionHtml = "";
    if (status.toLowerCase().includes("whois")) {
        statusClass = "unavailable-row";
        actionHtml = `<a href="https://whois.domaintools.com/${domain}" target="_blank">Whois</a>`;
    }
    else if (status.toLowerCase().includes("unavailable")) {
        statusClass = "unavailable-row";
        actionHtml = `<a href="http://${domain}" target="_blank">Visit</a>`;
    } else if (status.toLowerCase().includes("available")) {
        statusClass = "available-row";
        actionHtml = `<a href="https://www.godaddy.com/en-in/domainsearch/find?domainToCheck=${domain}" target="_blank">Buy</a>`;
    }

    row.className = statusClass;
    row.innerHTML = `
	<td>${currentIndex}</td>
	<td>${domain}</td>
	<td>${status}</td>
	<td>${domain.length}</td>
	<td>-</td>
	<td class="clm-whois">-</td>
	<td class="clm-resource">-</td>
	<td class="clm-exec-time">-</td>
	<td>${actionHtml}</td>
	`;

    if (status.toLowerCase().includes("unavailable") || status.toLowerCase().includes("invalid")) {
        row.classList.add("error");
    }
    tbody.appendChild(row);
});

connection.on("ProgressUpdate", (data) => {
    document.querySelector("#available-counter").textContent = data.available;
    document.querySelector("#unavailable-counter").textContent = data.unavailable;
    document.querySelector("#error-counter").textContent = data.error;

    document.getElementById("domains-checked").textContent = data.processed;
    document.getElementById("domains-length").textContent = data.total;
    document.getElementById("progress").textContent = Math.floor((data.processed / data.total) * 100);

    // Stop stopwatch if done
    if (data.processed >= data.total) {
        stopStopwatch();
    }
});

connection.start().then(() => {
    console.log("SignalR connected.");
    document.getElementById("availability-form").addEventListener("submit", function (e) {
        e.preventDefault();

        const domainText = document.querySelector("textarea[name='domains']").value;
        const tldOptions = document.querySelector("select[name='tlds']").selectedOptions;
        const domains = domainText.split(/[\s\n]+/).filter(Boolean);
        const tlds = Array.from(tldOptions).map(opt => opt.value);

        const requestData = { domains, tlds };
        totalDomains = tlds.length ? domains.length * tlds.length : domains.length;

        // Reset progress
        document.getElementById("domains-length").textContent = totalDomains;
        document.getElementById("domains-checked").textContent = "0";
        document.getElementById("progress").textContent = "0";
        document.querySelector("#result-table tbody").innerHTML = "";
        document.querySelector(".result").classList.remove("hidden");
        document.querySelector("#stopBtn").classList.remove("hidden");
        document.querySelector("#pause").classList.remove("hidden");

        startStopwatch(); // 🔹 START stopwatch

        fetch("/Home/CheckDomains", {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(requestData)
        }).then(res => {
            if (!res.ok) throw new Error("Failed to check domains");
        }).catch(err => {
            alert("Error: " + err.message);
        });
    });
}).catch(err => {
    console.error("SignalR connection failed:", err.toString());
});

document.getElementById("hide-unavailable").addEventListener("change", function () {
    const hide = this.checked;
    document.querySelectorAll("#result-table tbody tr.unavailable-row").forEach(row => {
        row.style.display = hide ? "none" : "";
    });
});

document.getElementById("exportBtn").addEventListener("click", function () {
    $("#result-table").table2csv("download", { filename: "domain-results.csv" });
});

document.getElementById("stopBtn").addEventListener("click", function () {
    fetch("/Home/CancelCheck", { method: "POST" })
        .then(() => {
            stopStopwatch();
            this.classList.add("hidden");
            document.getElementById("pause").classList.add("hidden");
            document.getElementById("resumeBtn").classList.add("hidden");
        })
        .catch(err => alert("Cancel error: " + err.message));
});

document.getElementById("pause").addEventListener("click", function () {
    fetch("/Home/PauseCheck", { method: "POST" })
        .then(() => {
            clearInterval(stopwatchInterval);
            this.classList.add("hidden");
            document.getElementById("resumeBtn").classList.remove("hidden");
        })
        .catch(err => alert("Pause error: " + err.message));
});

document.getElementById("resumeBtn").addEventListener("click", function () {
    fetch("/Home/ResumeCheck", { method: "POST" })
        .then(() => {
            startStopwatch();
            this.classList.add("hidden");
            document.getElementById("pause").classList.remove("hidden");
        })
        .catch(err => alert("Resume error: " + err.message));
});

document.querySelector("form").addEventListener("reset", () => {
    stopStopwatch();
    document.getElementById('stopwatch').textContent = '00:00';
});

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
            stopStopwatch(); // 🔹 reset on import
            document.getElementById('stopwatch').textContent = '00:00';
        })
        .catch(error => alert("Error: " + error.message));
}
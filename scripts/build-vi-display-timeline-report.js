const fs = require("fs");
const path = require("path");
const readline = require("readline");

function parseArgs(argv) {
  const result = {};
  for (let i = 2; i < argv.length; i++) {
    const arg = argv[i];
    if (!arg.startsWith("--")) {
      continue;
    }

    const key = arg.substring(2);
    const next = argv[i + 1];
    if (next === undefined || next.startsWith("--")) {
      result[key] = "true";
    } else {
      result[key] = next;
      i++;
    }
  }

  return result;
}

function resolvePath(value, fallback) {
  const target = value && value.trim().length > 0 ? value : fallback;
  return path.resolve(target);
}

function parseCsvLine(line) {
  const result = [];
  let current = "";
  let quoted = false;

  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (quoted) {
      if (ch === '"') {
        if (line[i + 1] === '"') {
          current += '"';
          i++;
        } else {
          quoted = false;
        }
      } else {
        current += ch;
      }
    } else if (ch === '"') {
      quoted = true;
    } else if (ch === ",") {
      result.push(current);
      current = "";
    } else {
      current += ch;
    }
  }

  result.push(current);
  return result;
}

function readCsv(pathName) {
  if (!fs.existsSync(pathName)) {
    return [];
  }

  const lines = fs.readFileSync(pathName, "utf8").split(/\r?\n/).filter((line) => line.length > 0);
  if (lines.length === 0) {
    return [];
  }

  const headers = parseCsvLine(lines[0]);
  return lines.slice(1).map((line) => {
    const fields = parseCsvLine(line);
    const row = {};
    for (let i = 0; i < headers.length; i++) {
      row[headers[i]] = fields[i] ?? "";
    }

    return row;
  });
}

function csvEscape(value) {
  if (value === null || value === undefined) {
    return "";
  }

  const text = String(value);
  if (text.includes(",") || text.includes('"') || text.includes("\n") || text.includes("\r")) {
    return `"${text.replace(/"/g, '""')}"`;
  }

  return text;
}

function writeCsv(pathName, rows, headers) {
  const content = [
    headers.map(csvEscape).join(","),
    ...rows.map((row) => headers.map((header) => csvEscape(row[header])).join(",")),
  ].join("\n") + "\n";
  fs.writeFileSync(pathName, content, "utf8");
}

function parseNullableNumber(value) {
  if (value === null || value === undefined) {
    return null;
  }

  const text = String(value).trim();
  if (text.length === 0) {
    return null;
  }

  if (/^\+?0x/i.test(text)) {
    return Number.parseInt(text.replace(/^\+?0x/i, ""), 16);
  }

  const parsed = Number.parseInt(text, 10);
  return Number.isFinite(parsed) ? parsed : null;
}

function toUint32(value) {
  const parsed = parseNullableNumber(value);
  if (parsed === null || !Number.isFinite(parsed)) {
    return null;
  }

  return parsed >>> 0;
}

function formatHex32(value) {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "";
  }

  return `0x${(value >>> 0).toString(16).toUpperCase().padStart(8, "0")}`;
}

function formatFifoOffset(value) {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "";
  }

  return `+0x${Math.trunc(value).toString(16).toUpperCase()}`;
}

function isMainRamPhysicalAddress(address) {
  return address > 0 && address < 0x01800000;
}

function toPhysicalAddress(address) {
  if (address === null || address === undefined || !Number.isFinite(address)) {
    return null;
  }

  const value = address >>> 0;
  if (value >= 0x80000000 && value < 0x81800000) {
    return value - 0x80000000;
  }

  if (value >= 0xC0000000 && value < 0xC1800000) {
    return value - 0xC0000000;
  }

  return value;
}

function normalizeViAddress(value, preferShifted) {
  const number = toUint32(value);
  if (number === null) {
    return null;
  }

  const physical = toPhysicalAddress(number);
  const shifted = toPhysicalAddress(((number & 0x00FFFFFF) << 5) >>> 0);
  if (shifted !== null && preferShifted && shifted !== physical && isMainRamPhysicalAddress(shifted)) {
    return shifted;
  }

  if (physical !== null && isMainRamPhysicalAddress(physical)) {
    return physical;
  }

  if (shifted !== null && !preferShifted && shifted !== physical && isMainRamPhysicalAddress(shifted)) {
    return shifted;
  }

  return null;
}

const viRegisterNames = new Map([
  [0xCC00201C, "field0_high"],
  [0xCC00201E, "field0_low"],
  [0xCC002024, "field1_high"],
  [0xCC002026, "field1_low"],
  [0xCC002020, "field2_high"],
  [0xCC002022, "field2_low"],
  [0xCC002028, "field3_high"],
  [0xCC00202A, "field3_low"],
]);

const viPairs = [
  { name: "field0", high: 0xCC00201C, low: 0xCC00201E },
  { name: "field1", high: 0xCC002024, low: 0xCC002026 },
  { name: "field2", high: 0xCC002020, low: 0xCC002022 },
  { name: "field3", high: 0xCC002028, low: 0xCC00202A },
];

function getViRegisterName(address) {
  return viRegisterNames.get(address >>> 0) ?? "";
}

function getViResolvedAddresses(registerValues) {
  const result = {
    field0: null,
    field1: null,
    field2: null,
    field3: null,
  };

  for (const pair of viPairs) {
    if (registerValues.has(pair.high) && registerValues.has(pair.low)) {
      const combined = (((registerValues.get(pair.high) & 0xFF) << 16) | (registerValues.get(pair.low) & 0xFFFF)) >>> 0;
      result[pair.name] = normalizeViAddress(combined, false);
    }
  }

  for (const address of [0xCC00201C, 0xCC002024, 0xCC002020, 0xCC002028]) {
    if (registerValues.has(address)) {
      const direct = normalizeViAddress(registerValues.get(address), true);
      const name = getViRegisterName(address).replace("_high", "");
      if (direct !== null && name.length > 0 && result[name] === null) {
        result[name] = direct;
      }
    }
  }

  return result;
}

function convertCopyRow(row) {
  if (row.kind !== "display") {
    return null;
  }

  const fifoOffset = parseNullableNumber(row.fifo_offset);
  if (fifoOffset === null) {
    return null;
  }

  const displayAddress = toUint32(row.display_address || row.destination_address);
  return {
    copy_index: parseNullableNumber(row.copy_index),
    fifo_offset: fifoOffset,
    fifo_offset_text: formatFifoOffset(fifoOffset),
    draws_seen: parseNullableNumber(row.draws_seen),
    display_address: displayAddress,
    display_address_text: formatHex32(displayAddress),
    display_nonblack: parseNullableNumber(row.display_nonblack),
    display_nonblack_percent: row.display_nonblack_percent ?? "",
    display_nonblack_bounds: row.display_nonblack_bounds ?? "",
    source_width: row.src_width ?? "",
    source_height: row.src_height ?? "",
    display_width: row.display_width ?? "",
    display_height: row.display_height ?? "",
    clear: row.clear ?? "",
    instruction: "",
    pc: "",
    opcode: "",
    disassembly: "",
    vi_field: "",
    vi_field0: "",
    vi_field1: "",
    vi_field2: "",
    vi_field3: "",
    first_vi_match_instruction: "",
    first_vi_match_field: "",
    first_vi_match_delta_instructions: "",
  };
}

function parseMmioTraceLine(line) {
  const quoteStart = line.indexOf('"');
  if (quoteStart < 0) {
    return null;
  }

  const quoteEnd = line.indexOf('",', quoteStart + 1);
  if (quoteEnd < 0) {
    return null;
  }

  const prefix = line.substring(0, quoteStart).replace(/,$/, "");
  const prefixFields = prefix.split(",");
  if (prefixFields.length < 3) {
    return null;
  }

  const rest = line.substring(quoteEnd + 2).split(",");
  if (rest.length < 5) {
    return null;
  }

  return {
    instruction: parseNullableNumber(prefixFields[0]),
    pc: prefixFields[1],
    opcode: prefixFields[2],
    disassembly: line.substring(quoteStart + 1, quoteEnd).replace(/""/g, '"'),
    device: rest[0],
    kind: rest[1],
    width: parseNullableNumber(rest[2]),
    address: toUint32(rest[3]),
    value: toUint32(rest[4]),
  };
}

function makeCopyOutputRow(copy) {
  return {
    copy_index: copy.copy_index ?? "",
    fifo_offset: copy.fifo_offset_text,
    draws_seen: copy.draws_seen ?? "",
    instruction: copy.instruction ?? "",
    pc: copy.pc ?? "",
    display_address: copy.display_address_text,
    display_nonblack: copy.display_nonblack ?? "",
    display_nonblack_percent: copy.display_nonblack_percent,
    display_nonblack_bounds: copy.display_nonblack_bounds,
    vi_field: copy.vi_field,
    vi_field0: copy.vi_field0,
    vi_field1: copy.vi_field1,
    vi_field2: copy.vi_field2,
    vi_field3: copy.vi_field3,
    first_vi_match_instruction: copy.first_vi_match_instruction,
    first_vi_match_field: copy.first_vi_match_field,
    first_vi_match_delta_instructions: copy.first_vi_match_delta_instructions,
    source_width: copy.source_width,
    source_height: copy.source_height,
    display_width: copy.display_width,
    display_height: copy.display_height,
    clear: copy.clear,
  };
}

async function scanMmioTrace(mmioTracePath, displayCopies) {
  const viRows = [];
  const registerValues = new Map();
  let gxFifoBytes = 0;
  let nextCopyIndex = 0;
  let scannedRows = 0;
  let gxFifoWrites = 0;
  let viWrites = 0;

  function recordFirstViMatches(row, resolved) {
    for (const copy of displayCopies) {
      if (copy.display_address === null || copy.instruction === "" || copy.first_vi_match_instruction !== "") {
        continue;
      }

      for (const name of ["field0", "field1", "field2", "field3"]) {
        if (resolved[name] === copy.display_address) {
          copy.first_vi_match_instruction = row.instruction ?? "";
          copy.first_vi_match_field = name;
          copy.first_vi_match_delta_instructions =
            row.instruction !== null && row.instruction !== undefined && copy.instruction !== ""
              ? row.instruction - Number(copy.instruction)
              : "";
          break;
        }
      }
    }
  }

  function hasMappedAndDisplayedEveryCopy() {
    return displayCopies.every((copy) => copy.instruction !== "" && copy.first_vi_match_instruction !== "");
  }

  if (!fs.existsSync(mmioTracePath)) {
    return { viRows, scannedRows, gxFifoWrites, viWrites, gxFifoBytes };
  }

  const reader = readline.createInterface({
    input: fs.createReadStream(mmioTracePath, { encoding: "utf8" }),
    crlfDelay: Infinity,
  });

  let first = true;
  for await (const line of reader) {
    if (first) {
      first = false;
      continue;
    }

    if (!line.includes(",VI,") && !line.includes(",GX FIFO,")) {
      continue;
    }

    scannedRows++;
    const row = parseMmioTraceLine(line);
    if (row === null) {
      continue;
    }

    if (row.device === "VI" && row.kind === "Write" && row.address !== null && row.value !== null) {
      viWrites++;
      registerValues.set(row.address, row.value);
      const resolved = getViResolvedAddresses(registerValues);
      recordFirstViMatches(row, resolved);
      viRows.push({
        instruction: row.instruction ?? "",
        pc: row.pc,
        opcode: row.opcode,
        disassembly: row.disassembly,
        width: row.width ?? "",
        address: formatHex32(row.address),
        register: getViRegisterName(row.address),
        value: formatHex32(row.value),
        field0: formatHex32(resolved.field0),
        field1: formatHex32(resolved.field1),
        field2: formatHex32(resolved.field2),
        field3: formatHex32(resolved.field3),
      });

      if (displayCopies.length > 0 && hasMappedAndDisplayedEveryCopy()) {
        reader.close();
        break;
      }
    }

    if (row.device === "GX FIFO" && row.kind === "Write" && row.width !== null) {
      gxFifoWrites++;
      gxFifoBytes += row.width;
      while (nextCopyIndex < displayCopies.length && displayCopies[nextCopyIndex].fifo_offset < gxFifoBytes) {
        const copy = displayCopies[nextCopyIndex++];
        const resolved = getViResolvedAddresses(registerValues);
        copy.instruction = row.instruction ?? "";
        copy.pc = row.pc;
        copy.opcode = row.opcode;
        copy.disassembly = row.disassembly;
        copy.vi_field0 = formatHex32(resolved.field0);
        copy.vi_field1 = formatHex32(resolved.field1);
        copy.vi_field2 = formatHex32(resolved.field2);
        copy.vi_field3 = formatHex32(resolved.field3);

        for (const name of ["field0", "field1", "field2", "field3"]) {
          if (copy.display_address !== null && resolved[name] === copy.display_address) {
            copy.vi_field = name;
            copy.first_vi_match_instruction = row.instruction ?? "";
            copy.first_vi_match_field = name;
            copy.first_vi_match_delta_instructions = 0;
            break;
          }
        }
      }

      if (displayCopies.length > 0 && hasMappedAndDisplayedEveryCopy()) {
        reader.close();
        break;
      }
    }
  }

  return { viRows, scannedRows, gxFifoWrites, viWrites, gxFifoBytes };
}

function buildSelectedRows(gxFrameSweepPath, copyRows) {
  if (!fs.existsSync(gxFrameSweepPath)) {
    return [];
  }

  return readCsv(gxFrameSweepPath)
    .filter((row) => (row.selected_copy_index ?? "").trim().length > 0)
    .map((row) => {
      const selectedAddress = toUint32(row.selected_copy_destination_address);
      const selectedDrawsSeen = row.selected_copy_draws_seen ?? "";
      const selectedAddressText = formatHex32(selectedAddress);
      const matchedCopy = copyRows.find((copy) => copy.display_address === selectedAddressText && String(copy.draws_seen) === String(selectedDrawsSeen));
      return {
        skip: row.skip ?? "",
        path: row.path ?? "",
        source: row.source ?? "",
        source_copy_index: row.source_copy_index ?? "",
        selected_copy_index: row.selected_copy_index ?? "",
        selected_copy_kind: row.selected_copy_kind ?? "",
        selected_copy_draws_seen: selectedDrawsSeen,
        selected_copy_fifo_offset: row.selected_copy_fifo_offset ?? "",
        selected_copy_destination_address: selectedAddressText,
        lifecycle_phase: row.lifecycle_phase ?? "",
        matched_display_copy: matchedCopy ? matchedCopy.copy_index : "",
      };
    });
}

async function main() {
  const args = parseArgs(process.argv);
  const runRoot = resolvePath(args["run-root"], process.cwd());
  const gxCopiesPath = resolvePath(args["gx-copies"], path.join(runRoot, "gx-copies.csv"));
  const mmioTracePath = resolvePath(args.mmio, path.join(runRoot, "mmio.csv"));
  const gxFrameSweepPath = resolvePath(args["gx-frame-sweep"], path.join(runRoot, "gx-frame-sweep.csv"));
  const outputDirectory = resolvePath(args.out, path.join(runRoot, "vi-display-timeline"));

  fs.mkdirSync(outputDirectory, { recursive: true });
  if (!fs.existsSync(gxCopiesPath)) {
    throw new Error(`GX copy CSV not found: ${gxCopiesPath}`);
  }

  const displayCopies = readCsv(gxCopiesPath)
    .map(convertCopyRow)
    .filter((row) => row !== null)
    .sort((a, b) => a.fifo_offset - b.fifo_offset);

  const scan = await scanMmioTrace(mmioTracePath, displayCopies);
  const copyRows = displayCopies.map(makeCopyOutputRow);
  const selectedRows = buildSelectedRows(gxFrameSweepPath, copyRows);

  const viWritePath = path.join(outputDirectory, "vi-register-writes.csv");
  const displayJoinPath = path.join(outputDirectory, "display-copy-vi-join.csv");
  const selectedPath = path.join(outputDirectory, "selected-frame-copy-join.csv");
  const reportPath = path.join(outputDirectory, "vi-display-timeline-report.json");

  writeCsv(viWritePath, scan.viRows, [
    "instruction",
    "pc",
    "opcode",
    "disassembly",
    "width",
    "address",
    "register",
    "value",
    "field0",
    "field1",
    "field2",
    "field3",
  ]);
  writeCsv(displayJoinPath, copyRows, [
    "copy_index",
    "fifo_offset",
    "draws_seen",
    "instruction",
    "pc",
    "display_address",
    "display_nonblack",
    "display_nonblack_percent",
    "display_nonblack_bounds",
    "vi_field",
    "vi_field0",
    "vi_field1",
    "vi_field2",
    "vi_field3",
    "first_vi_match_instruction",
    "first_vi_match_field",
    "first_vi_match_delta_instructions",
    "source_width",
    "source_height",
    "display_width",
    "display_height",
    "clear",
  ]);
  writeCsv(selectedPath, selectedRows, [
    "skip",
    "path",
    "source",
    "source_copy_index",
    "selected_copy_index",
    "selected_copy_kind",
    "selected_copy_draws_seen",
    "selected_copy_fifo_offset",
    "selected_copy_destination_address",
    "lifecycle_phase",
    "matched_display_copy",
  ]);

  const report = {
    schema: "ngcsharp.vi-display-timeline.v1",
    runRoot,
    gxCopiesPath,
    mmioTracePath: fs.existsSync(mmioTracePath) ? mmioTracePath : null,
    gxFrameSweepPath: fs.existsSync(gxFrameSweepPath) ? gxFrameSweepPath : null,
    displayCopies: displayCopies.length,
    displayCopiesWithInstruction: copyRows.filter((row) => String(row.instruction).length > 0).length,
    displayCopiesWithViFieldMatch: copyRows.filter((row) => String(row.vi_field).length > 0).length,
    displayCopiesWithEventualViFieldMatch: copyRows.filter((row) => String(row.first_vi_match_field).length > 0).length,
    viWrites: scan.viRows.length,
    selectedFrameRows: selectedRows.length,
    scannedMmioRows: scan.scannedRows,
    gxFifoWrites: scan.gxFifoWrites,
    gxFifoBytes: scan.gxFifoBytes,
    viRegisterWritesCsvPath: viWritePath,
    displayCopyViJoinCsvPath: displayJoinPath,
    selectedFrameCopyJoinCsvPath: selectedPath,
  };
  fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");

  console.log(`VI display timeline report: ${reportPath}`);
  for (const row of copyRows) {
    const eventual = row.first_vi_match_field
      ? `first displayed by ${row.first_vi_match_field} at ${row.first_vi_match_instruction} (+${row.first_vi_match_delta_instructions})`
      : "not displayed in trace";
    console.log(`${row.copy_index || ""}\t${row.fifo_offset}\tdraw ${row.draws_seen || ""}\tinstruction ${row.instruction || ""}\t${row.display_address}\t${row.display_nonblack_percent}\tat-copy ${row.vi_field || "(no VI match)"}\t${eventual}`);
  }
}

main().catch((error) => {
  console.error(error && error.stack ? error.stack : error);
  process.exitCode = 1;
});

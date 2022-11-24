"use-strict;"
/* eslint-disable @typescript-eslint/no-var-requires */
const dotenv = require("dotenv");
const fs = require("fs");
const os = require("os");


let envFilePath = ".env"
let configRoot = "ENV_CONFIG"
let outputFile = "./public/env-config.js"

for (let i = 2; i < process.argv.length; i++) {
    switch (process.argv[i]) {
        case "-e":
            envFilePath = process.argv[++i]
        break;
        case "-o":
            outputFile = process.argv[++i]
        break;
        case "-c":
            configRoot = process.argv[++i]
        break;
        default:
            throw Error(`unknown option ${process.argv[i]}`)
    }
}

if (fs.existsSync(envFilePath)) {
    console.log(`Loading environment file from '${envFilePath}'`)

    dotenv.config({
        path: envFilePath
    })    
}

console.log(`Generating JS configuration output to: ${outputFile}`)
console.log(`Current directory is: ${process.cwd()}`)

fs.writeFileSync(outputFile, `window.${configRoot} = {${os.EOL}${
    Object.keys(process.env).filter(x => x.startsWith("REACT_APP_")).map(key => {
        console.log(`- Found '${key}'`);
        return `${key}: '${process.env[key]}',${os.EOL}`;
    }).join("")
}${os.EOL}}`);
import Anthropic from "@anthropic-ai/sdk";
import fs from "fs";

const client = new Anthropic({
  apiKey: process.env.ANTHROPIC_API_KEY,
});

// read file from terminal argument
const filePath = process.argv[2];

if (!filePath) {
  console.log("Usage: node claude.js <file-path>");
  process.exit(1);
}

const fileContent = fs.readFileSync(filePath, "utf-8");

const response = await client.messages.create({
  model: "claude-3-5-sonnet-latest",
  max_tokens: 2000,
  messages: [
    {
      role: "user",
      content: `
You are a senior software engineer.

Refactor and improve this code for production use.
Keep architecture clean and scalable.

CODE:
${fileContent}
      `,
    },
  ],
});

console.log(response.content[0].text);
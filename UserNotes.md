# Notes

A list of my notes to talk about in the future

## Items

- lets go through our razor pages and identify large files that probably should be broken up into smaller reusable components

refactor users page to have the table users display be a separate reusable component

find methods with bools in their params and discuss ways of reducing that smell

find any files with multiple classes in the same file and split, as well as any files where the class name doesnt match the file name and fix the file

New wasm ui.  use of both logger and console.writeline.  standardize on one

create a template for the ui conversion.  

follow up and find all places where we use ! as a null forgiving operator to make sure its used properly in places where its never possible to get null. otherwise add defensive guards

rethink translation service flow

chatbot integration for talking with ai about internal data like messages, analytics, and logs

create feasibility document for adding matrix chats to the app

Actor.Cas => FromSystem("cas") static property + "cas" => "CAS (Combot Anti-Spam)" display name:  look at cleaning this up